using System.Numerics;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.Logging.Models;
using Zaya.OCR.Services;
using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

[Log(LogLevel.Debug)]
public sealed class DocumentScanService : IDocumentScanService
{
    private readonly IOCRService _ocr;
    private readonly ITextLayoutService _layout;

    public DocumentScanService(IOCRService ocr, ITextLayoutService layout)
    {
        _ocr = ocr;
        _layout = layout;
    }

    public async Task<ScanResult> ScanAsync(
        IRawImage page,
        DocumentScanOptions options,
        IReadOnlyDictionary<string, object>? ocrSettings = null,
        IReadOnlyDictionary<string, object>? textLayoutSettings = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inkThreshold = InkDensityEstimator.ComputePageInkThreshold(page);

        using var ocrSession = ocrSettings is { Count: > 0 }
            ? await _ocr.CreateSessionAsync(ocrSettings, cancellationToken).ConfigureAwait(false)
            : await _ocr.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // One quick OCR pass over the whole page (padded, so nothing sits at an edge) before the
        // sliding scan: it measures the page's line pitch for the window size, and since it's the
        // one frame that sees a rotated stamp whole, its rotated words are added to the stamp
        // observations too (no extra cost - the pass is needed for the window size anyway).
        var wholePage = await RecognizeWholePageAsync(ocrSession, page, cancellationToken).ConfigureAwait(false);
        // Window height and step are both in units of this page's own line pitch: on a test set
        // the best window was ~6-10 lines whatever the text size, and stepping 2 lines per frame
        // matched 15px stepping in quality at 3-4x the speed (quality held up to ~3 lines; half a
        // line was worse - each line passed through so many frames that its readings fragmented).
        var pitch = EstimateLinePitch(wholePage);
        var windowHeight = options.WindowHeight
            ?? (pitch is { } p ? Math.Clamp((int)Math.Round(options.WindowLines * p), 48, page.Height + 2 * WholePagePad) : Math.Max(1, page.Height / 4));
        var step = pitch is { } q ? Math.Max(1, (int)Math.Round(q * options.StepPercentOfLine / 100.0)) : FallbackStepPx;

        // A line sitting right at the very top or bottom of the real page would otherwise never
        // get a window position clear of both edges - the window can't start before row 0 or end
        // past the last row, so such a line is stuck in the margin band for every frame it's ever
        // visible in. Padding with blank rows on both sides lets the window start and end beyond
        // the real page, giving every line - including ones at the very edge of the content -
        // the same kind of safe, centered pass that an interior line gets for free.
        var padding = windowHeight / 2;

        // The window-slide step splits genuinely wide gaps (label/value lists) unusually far
        // apart, so scanning always needs its own wordGapThreshold regardless of whatever the
        // user configured for the text-layout engine elsewhere — it wins over a user override.
        var mergedTextLayoutSettings = textLayoutSettings is { Count: > 0 }
            ? new Dictionary<string, object>(textLayoutSettings)
            : [];
        mergedTextLayoutSettings["wordGapThreshold"] = options.WordGapThresholdPercent;
        mergedTextLayoutSettings["respectOcrLines"] = options.RespectOcrLines;

        using var layoutSession = await _layout.CreateSessionAsync(mergedTextLayoutSettings, cancellationToken).ConfigureAwait(false);

        var tracked = new Dictionary<Guid, TrackedLine>();
        var committed = new List<TrackedLine>();
        var rotatedObservations = new RotatedTextClusterer.ObservationSet();

        var (_, wholePageRotated) = RotatedTextClusterer.SplitByAngle(wholePage.Words, options.MaxWordAngleDeviationDegrees);
        if (wholePageRotated.Count > 0)
        {
            var offset = new OffsetOcrResult(wholePageRotated, wholePage.Confidence, new Vector2(0, -WholePagePad));
            rotatedObservations.Add(offset.Words, -WholePagePad, page.Height + 2 * WholePagePad);
        }

        for (var y = -padding; y + windowHeight <= page.Height + padding; y += step)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var crop = RawImageCropper.CropRowsPadded(page, y, windowHeight);
            var ocrResult = await ocrSession.RecognizeAsync(crop, cancellationToken).ConfigureAwait(false);

            // Words whose own angle marks them as rotated content (e.g. a round stamp's text,
            // which follows the curve) are set aside here, before line assembly ever sees them -
            // forcing them into a horizontal line would either mangle them or lose them outright.
            // They're collected separately (RotatedTextClusterer) and rendered as their own
            // rotated text blocks instead, not fed to the straight-line word-to-line assembler.
            var (normalWords, outlierWords) = RotatedTextClusterer.SplitByAngle(
                ocrResult.Words, options.MaxWordAngleDeviationDegrees);

            // The crop already includes the margin rows (OCR gets full context, nothing is
            // pre-clipped), and the layout engine links lines across frames on the whole set -
            // only afterward does TrustCenter decide which resulting lines are trustworthy.
            var offset = new OffsetOcrResult(normalWords, ocrResult.Confidence, new Vector2(0, y));
            var textResult = await layoutSession.ProcessAsync(offset, cancellationToken).ConfigureAwait(false);

            var trustedLines = TrustCenter.FilterEntirelyBuffered(
                textResult.Paragraphs.SelectMany(p => p.Lines), y, windowHeight, options.MarginMultiplier);

            Accumulate(trustedLines, tracked, committed, options.MissingFrameGrace, page, inkThreshold, y, windowHeight, options.MarginMultiplier);

            if (outlierWords.Count > 0)
            {
                var offsetOutliers = new OffsetOcrResult(outlierWords, ocrResult.Confidence, new Vector2(0, y));
                rotatedObservations.Add(offsetOutliers.Words, y, windowHeight);
            }

            if (progress is not null)
            {
                var wordsSoFar = tracked.Values.SelectMany(t => t.Words)
                    .Concat(committed.SelectMany(c => c.Words))
                    .ToList();
                progress.Report(new ScanProgress(y, windowHeight, wordsSoFar));
            }
        }

        committed.AddRange(tracked.Values);
        var clusters = rotatedObservations.BuildClusters();
        return new ScanResult(RemoveRotatedDuplicates(Dedupe(committed), clusters), clusters);
    }

    /// <summary>
    /// Updates the tracked-line table with this frame's lines (keeping the longest reading seen
    /// across each line's lifetime — a tilted source line can lose trailing words to the
    /// trust-center margin one frame before it exits the window, and a plain "latest wins"
    /// update would let that one bad frame clobber many good preceding reads), then commits and
    /// drops any previously tracked line that didn't reappear this frame for long enough.
    ///
    /// <paramref name="lines"/> has already been through TrustCenter, so every line here has at
    /// least reached the trusted core once - there's no truncated-first-sighting risk left to
    /// guard against on admission. Eviction still respects a buffer band near the window's top
    /// edge: a previously tracked line does not lose grace-period credit for going quiet while
    /// it's passing through that band on its way out - TrustCenter drops it there on purpose, so
    /// that silence is expected, not a real disappearance.
    /// </summary>
    private static void Accumulate(
        IEnumerable<Zaya.Primitives.OCR.ITextLine> lines,
        Dictionary<Guid, TrackedLine> tracked,
        List<TrackedLine> committed,
        int missingFrameGrace,
        IRawImage page,
        double inkThreshold,
        int windowY,
        int windowHeight,
        float marginMultiplier)
    {
        var seenThisFrame = new HashSet<Guid>();
        foreach (var line in lines)
        {
            seenThisFrame.Add(line.Id);
            var heights = CleanWordHeights(line, windowY, windowHeight);

            if (tracked.TryGetValue(line.Id, out var existing))
            {
                if (line.Text.Trim().Length >= existing.Text.Trim().Length)
                {
                    existing.Text = line.Text;
                    existing.MinX = line.Bounds.MinX;
                    existing.MaxX = line.Bounds.MaxX;
                    existing.Height = line.Bounds.TextHeight;
                    existing.Words = ToScannedWords(line.Words, page, inkThreshold);
                }

                existing.TopY = line.Bounds.MinY;
                existing.WordHeights.AddRange(heights);
                existing.MissingFrames = 0;
                existing.FrameCount++;
                continue;
            }

            tracked[line.Id] = new TrackedLine
            {
                Text = line.Text,
                TopY = line.Bounds.MinY,
                MinX = line.Bounds.MinX,
                MaxX = line.Bounds.MaxX,
                Height = line.Bounds.TextHeight,
                Words = ToScannedWords(line.Words, page, inkThreshold),
                WordHeights = heights,
                MissingFrames = 0,
                FrameCount = 1,
            };
        }

        foreach (var (id, state) in tracked.ToList())
        {
            if (seenThisFrame.Contains(id))
                continue;

            var margin = state.Height > 0 ? state.Height * marginMultiplier : 20f;
            var windowLocalTop = state.TopY - windowY;
            if (windowLocalTop < margin)
                continue;

            state.MissingFrames++;
            if (state.MissingFrames <= missingFrameGrace)
                continue;

            committed.Add(state);
            tracked.Remove(id);
        }
    }

    /// <summary>
    /// Cross-frame line linking occasionally misses a hop (the layout engine only compares
    /// against the immediately preceding frame - see <see cref="Accumulate"/>'s own doc comment
    /// for why that's structural, not a bug), re-identifying the same physical line as "new" with
    /// a fresh Id. The engine itself can never catch this - by the time the second Id starts, its
    /// one-frame-deep history has already moved past the first Id entirely - so this is the one
    /// place in the pipeline that can: it's the only place with every line the whole scan ever
    /// committed in view at once, not just the last frame.
    ///
    /// Two lines occupying the same physical spot (<see cref="IsSamePosition"/>) are collapsed.
    /// Two shapes of duplicate show up there: one Id's reading is a truncated fragment of the
    /// other's (substring match - keep the longer one, the same rule the engine itself uses
    /// internally), or the hop happened after the line was already fully readable on both sides,
    /// so both readings are full-length and differ only by a couple of OCR-ambiguous characters
    /// (e.g. "0" vs "О") - neither is a substring of the other, so that needs a fuzzy
    /// near-duplicate check instead. For a fuzzy match there's no text-based way to tell which
    /// reading is more correct, so the tie-breaker is which Id was actually seen in more frames -
    /// a one-frame misread is less likely to dominate its line's frame count than the reading
    /// most of the pass agreed on.
    /// </summary>
    private static IReadOnlyList<ScannedLine> Dedupe(List<TrackedLine> committed)
    {
        // While tracking, TopY follows wherever this Id was seen last - needed for eviction, but
        // an Id the layout engine re-linked to a neighboring line can end up there while its
        // stored (longest) reading is of a different spot (a clinic logo's rail carried over to
        // the "Пол: Мужской" line beside it put that reading 40px too high, above "Возраст").
        // Only such a line - one whose kept words sit clearly away from its TopY - is moved to
        // where its words are; every other line keeps its rail-based TopY, which paragraph
        // grouping's gap thresholds are tuned against.
        foreach (var line in committed.Where(l => l.Words.Count > 0))
        {
            var wordsTop = line.Words.Min(w => w.MinY);
            var wordHeight = MathHelpers.Median(line.Words.Select(w => w.TextHeight).ToList());
            if (Math.Abs(wordsTop - line.TopY) > wordHeight)
            {
                line.TopY = wordsTop;
                line.MinX = line.Words.Min(w => w.MinX);
                line.MaxX = line.Words.Max(w => w.MaxX);
            }
        }

        var ordered = committed.OrderBy(l => l.TopY).ToList();

        var deduped = new List<TrackedLine>();
        foreach (var line in ordered)
        {
            var text = line.Text.Trim();
            if (text.Length == 0)
                continue;

            // Not just the last kept line: another line can sit between two readings of the same
            // one in Y order (a neighboring column's line at nearly the same height, or a
            // slightly mispositioned reading), so check every kept line still within the
            // same-position Y window.
            var duplicateOf = -1;
            for (var i = deduped.Count - 1; i >= 0 && line.TopY - deduped[i].TopY < SamePositionMaxDy; i--)
            {
                var prevText = deduped[i].Text.Trim();
                // Whole-string equality ignores case: OCR flips the case of a lone letter between
                // frames ("Ж" vs "ж"), and for a one-character string no edit-distance ratio can call
                // that a near match. The substring test stays case-sensitive - ignoring case there
                // swallowed the heading "Лечение" as a fragment of the line "лечение, коррекция...".
                var overlapsText = string.Equals(text, prevText, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(prevText, StringComparison.Ordinal)
                    || prevText.Contains(text, StringComparison.Ordinal);
                if (IsSamePosition(line, deduped[i]) && (overlapsText || IsNearDuplicate(text, prevText)))
                {
                    duplicateOf = i;
                    break;
                }

                // A one- or two-character line sitting on top of a word of another line is a
                // fragment of that word read in some frame as a line of its own - often with a
                // different glyph ("V" for the "У" of "У реки..."), so no text test can catch it.
                if (IsFragmentOf(line, deduped[i]))
                {
                    duplicateOf = i;
                    break;
                }

                if (IsFragmentOf(deduped[i], line))
                {
                    deduped[i] = line;
                    duplicateOf = -2;
                    break;
                }
            }

            if (duplicateOf == -2)
                continue;

            if (duplicateOf >= 0)
            {
                var prev = deduped[duplicateOf];
                var prevText = prev.Text.Trim();
                var preferCurrent = text.Length != prevText.Length
                    ? text.Length > prevText.Length
                    : line.FrameCount > prev.FrameCount;
                if (preferCurrent)
                    deduped[duplicateOf] = line;
                continue;
            }

            deduped.Add(line);
        }

        return deduped
            .Select(l => new ScannedLine(l.Text.Trim(), l.TopY, l.MinX, l.MaxX, l.Height, WithMeasuredHeights(l)))
            .ToList();
    }

    /// <summary>Scan step when the page has no text to measure a line pitch from.</summary>
    private const int FallbackStepPx = 15;

    /// <summary>
    /// This page's line pitch in px, measured from one quick OCR pass over the whole page (about a
    /// second - a small fraction of the scan itself), or null when it has no text. A fixed
    /// fraction of the page height is the wrong unit for the scan window: on a test set the best
    /// fraction for a low-resolution form with ~15px lines (1/12) left a homework sheet with ~40px
    /// lines only two or three lines per window, which produced duplicate readings, while the old
    /// default (1/4) was both slower and less accurate on small text.
    /// <para/>
    /// The pitch is the median vertical distance from each OCR line to the nearest line below it
    /// that overlaps it horizontally (so side-by-side columns don't count as neighbors), with line
    /// membership taken from the OCR engine's own line index when it reports one. Falls back to
    /// 1.6x the median word height.
    /// </summary>
    private static float? EstimateLinePitch(Zaya.Primitives.OCR.IOCRResult result)
    {
        if (result.Words.Count == 0)
            return null;

        var wordHeight = MathHelpers.Median(result.Words
            .Select(w => (Vector2.Distance(w.Bounds.P1, w.Bounds.P4) + Vector2.Distance(w.Bounds.P2, w.Bounds.P3)) / 2f)
            .ToList());

        var ocrLines = result.Words
            .Select((w, i) => (Word: w, Key: (w as Zaya.OCR.Models.IOCRWordExt)?.LineIndex ?? -1 - i))
            .GroupBy(x => x.Key)
            .Select(g => (
                CenterY: g.Average(x => (x.Word.Bounds.MinY + x.Word.Bounds.MaxY) / 2f),
                MinX: g.Min(x => x.Word.Bounds.MinX),
                MaxX: g.Max(x => x.Word.Bounds.MaxX)))
            .OrderBy(l => l.CenterY)
            .ToList();

        var gaps = new List<float>();
        for (var i = 0; i < ocrLines.Count; i++)
        {
            for (var j = i + 1; j < ocrLines.Count; j++)
            {
                var dy = ocrLines[j].CenterY - ocrLines[i].CenterY;
                if (dy < 0.5f * wordHeight)
                    continue;
                if (Math.Min(ocrLines[i].MaxX, ocrLines[j].MaxX) <= Math.Max(ocrLines[i].MinX, ocrLines[j].MinX))
                    continue;
                gaps.Add(dy);
                break;
            }
        }

        return gaps.Count > 0 ? MathHelpers.Median(gaps) : 1.6f * wordHeight;
    }

    /// <summary>Blank rows added above and below the page for the whole-page pass.</summary>
    private const int WholePagePad = 32;

    /// <summary>OCR of the whole page in one frame, padded by <see cref="WholePagePad"/> (coordinates are crop-relative).</summary>
    private static async Task<Zaya.Primitives.OCR.IOCRResult> RecognizeWholePageAsync(
        Zaya.OCR.Services.IOCRSession ocrSession, IRawImage page, CancellationToken cancellationToken)
    {
        using var crop = RawImageCropper.CropRowsPadded(page, -WholePagePad, page.Height + 2 * WholePagePad);
        return await ocrSession.RecognizeAsync(crop, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops words of ordinary lines that are just a stray copy of rotated-block text: the word's
    /// center lies inside the block's own rotated box and its text already appears in the block's
    /// text. The per-frame angle test sorting words into rotated vs. normal sees each sighting on
    /// its own, and a clipped or wobbly reading of a word at ~35° can measure just under the
    /// threshold in a frame or two (29.5° on a test stamp) - that reading then also comes out as
    /// normal text, alone or glued onto a nearby line ("Аллергоанамнез: отрицает доки"). Both
    /// conditions together keep normal text a stamp merely overlaps; words under 3 characters are
    /// never dropped, since a short word is too easily "contained" in any stamp text. A line with
    /// nothing left is removed.
    /// </summary>
    private static IReadOnlyList<ScannedLine> RemoveRotatedDuplicates(IReadOnlyList<ScannedLine> lines, IReadOnlyList<RotatedTextCluster> clusters)
    {
        if (clusters.Count == 0)
            return lines;

        var result = new List<ScannedLine>();
        foreach (var line in lines)
        {
            var kept = line.Words.Where(w => !clusters.Any(c => IsStrayCopy(w, c))).ToList();
            if (kept.Count == line.Words.Count)
                result.Add(line);
            else if (kept.Count > 0)
                result.Add(line with { Text = string.Join(" ", kept.Select(w => w.Text)), MinX = kept.Min(w => w.MinX), MaxX = kept.Max(w => w.MaxX), Words = kept });
        }

        return result;
    }

    private static bool IsStrayCopy(ScannedWord word, RotatedTextCluster cluster)
    {
        var text = word.Text.Trim();
        if (text.Length < 3 || !cluster.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
            return false;

        var center = new Vector2((word.MinX + word.MaxX) / 2f, (word.MinY + word.MaxY) / 2f) - new Vector2(cluster.CenterX, cluster.CenterY);
        var rad = cluster.AngleDegrees * (MathF.PI / 180f);
        var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        var normal = new Vector2(-dir.Y, dir.X);
        return Math.Abs(Vector2.Dot(center, dir)) <= cluster.Width * 0.6f
            && Math.Abs(Vector2.Dot(center, normal)) <= cluster.Height * 0.6f;
    }

    /// <summary>
    /// True when <paramref name="fragment"/> is at most two characters long and its center lies
    /// inside one of <paramref name="line"/>'s own word boxes. A real short line - a form value
    /// like "ж" beside its label, a list number like "1." - sits next to other text, not on top of
    /// one of its words, so it isn't touched.
    /// </summary>
    private static bool IsFragmentOf(TrackedLine fragment, TrackedLine line)
    {
        if (ReferenceEquals(fragment, line) || fragment.Words.Count == 0 || line.Words.Count == 0)
            return false;
        if (fragment.Text.Count(c => !char.IsWhiteSpace(c)) > 2 || line.Text.Trim().Length <= fragment.Text.Trim().Length)
            return false;

        var cx = (fragment.Words.Min(w => w.MinX) + fragment.Words.Max(w => w.MaxX)) / 2f;
        var cy = (fragment.Words.Min(w => w.MinY) + fragment.Words.Max(w => w.MaxY)) / 2f;
        return line.Words.Any(w => cx >= w.MinX && cx <= w.MaxX && cy >= w.MinY && cy <= w.MaxY);
    }

    /// <summary>Roughly one text line's height, in px.</summary>
    private const float SamePositionMaxDy = 60;

    /// <summary>
    /// True when two lines plausibly occupy the same physical spot on the page - close in Y
    /// (roughly one text line's height) AND overlapping in X by a meaningful fraction of the
    /// narrower one's width. Y-proximity alone isn't enough: two genuinely different lines (e.g.
    /// side-by-side columns) can sit at the same height without being the same line at all.
    /// </summary>
    private static bool IsSamePosition(TrackedLine a, TrackedLine b)
    {
        if (Math.Abs(a.TopY - b.TopY) >= SamePositionMaxDy)
            return false;

        var overlapX = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
        var narrowerWidth = Math.Min(a.MaxX - a.MinX, b.MaxX - b.MinX);
        return narrowerWidth > 0 && overlapX / narrowerWidth >= 0.3f;
    }

    /// <summary>
    /// True when two same-ish-length readings differ by only a small fraction of their
    /// characters - the signature of one or two OCR-ambiguous glyphs read differently across an
    /// Id hop, not two genuinely different lines that happen to sit close together.
    /// </summary>
    private static bool IsNearDuplicate(string a, string b)
    {
        var maxLength = Math.Max(a.Length, b.Length);
        if (maxLength == 0)
            return true;

        // Cheap floor before paying for edit distance: a mismatch bigger than the allowed ratio
        // in plain length difference alone can't possibly pass.
        const double maxDifferenceRatio = 0.15;
        if (Math.Abs(a.Length - b.Length) > maxLength * maxDifferenceRatio)
            return false;

        return LevenshteinDistance(a, b) <= maxLength * maxDifferenceRatio;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var previousRow = new int[b.Length + 1];
        var currentRow = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previousRow[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            currentRow[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[b.Length];
    }

    private static List<ScannedWord> ToScannedWords(
        IReadOnlyList<Zaya.Primitives.OCR.IOCRWord> words, IRawImage page, double inkThreshold)
        => words.Select(w => new ScannedWord(
            w.Text, w.Bounds.MinX, w.Bounds.MaxX, w.Bounds.MinY, w.Bounds.MaxY,
            InkDensityEstimator.MeasureDensity(page, w.Bounds.MinX, w.Bounds.MinY, w.Bounds.MaxX, w.Bounds.MaxY, inkThreshold),
            (Vector2.Distance(w.Bounds.P1, w.Bounds.P4) + Vector2.Distance(w.Bounds.P2, w.Bounds.P3)) / 2f))
            .ToList();

    private readonly record struct WordHeightSample(float MinX, float MaxX, float Height);

    /// <summary>
    /// A word's height measured in a frame where its box stays clear of both crop edges. A line is
    /// kept whole once any part of it reaches TrustCenter's core, so its other words - a tilted
    /// line's far end, say - can still sit right at the crop edge with their glyphs cut off: on a
    /// real scan such clipped readings measured 24-32px against ~40px for the same text, and the
    /// one frame whose reading "keep longest" stores can easily be one of those. Only the clean
    /// readings are kept, from every frame, for <see cref="WithMeasuredHeights"/>.
    /// </summary>
    private static List<WordHeightSample> CleanWordHeights(Zaya.Primitives.OCR.ITextLine line, int windowY, int windowHeight)
    {
        var result = new List<WordHeightSample>();
        foreach (var w in line.Words)
        {
            var height = (Vector2.Distance(w.Bounds.P1, w.Bounds.P4) + Vector2.Distance(w.Bounds.P2, w.Bounds.P3)) / 2f;
            var guard = height * EdgeGuardHeightFraction;
            if (w.Bounds.MinY - windowY < guard || windowY + windowHeight - w.Bounds.MaxY < guard)
                continue;
            result.Add(new WordHeightSample(w.Bounds.MinX, w.Bounds.MaxX, height));
        }

        return result;
    }

    /// <summary>How far (in its own heights) a word must stay from the crop edge for its height to count as unclipped.</summary>
    private const float EdgeGuardHeightFraction = 0.5f;

    /// <summary>
    /// The stored words with TextHeight replaced by the median of every clean reading whose center
    /// falls inside that word's horizontal extent - a per-word vote across frames instead of one
    /// frame's reading. A word with no clean reading keeps its own.
    /// </summary>
    private static List<ScannedWord> WithMeasuredHeights(TrackedLine line)
        => line.Words.Select(w =>
        {
            var samples = line.WordHeights
                .Where(h => (h.MinX + h.MaxX) / 2 >= w.MinX && (h.MinX + h.MaxX) / 2 <= w.MaxX)
                .Select(h => h.Height)
                .ToList();
            return samples.Count > 0 ? w with { TextHeight = MathHelpers.Median(samples) } : w;
        }).ToList();

    private sealed class TrackedLine
    {
        public required string Text { get; set; }
        public required float TopY { get; set; }
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float Height { get; set; }
        public List<ScannedWord> Words { get; set; } = [];

        /// <summary>Clean per-frame word height measurements (see <see cref="CleanWordHeights"/>), across every frame this Id was seen in.</summary>
        public List<WordHeightSample> WordHeights { get; set; } = [];

        public int MissingFrames { get; set; }

        /// <summary>How many frames this Id was actually seen in - used in Dedupe to break ties
        /// between two full-length near-duplicate readings left by an Id hop.</summary>
        public int FrameCount { get; set; }
    }
}
