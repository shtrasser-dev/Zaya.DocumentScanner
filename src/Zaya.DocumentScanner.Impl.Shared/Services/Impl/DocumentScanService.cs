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
        var windowHeight = options.WindowHeight ?? Math.Max(1, page.Height / 4);
        var inkThreshold = InkDensityEstimator.ComputePageInkThreshold(page);

        // A line sitting right at the very top or bottom of the real page would otherwise never
        // get a window position clear of both edges - the window can't start before row 0 or end
        // past the last row, so such a line is stuck in the margin band for every frame it's ever
        // visible in. Padding with blank rows on both sides lets the window start and end beyond
        // the real page, giving every line - including ones at the very edge of the content -
        // the same kind of safe, centered pass that an interior line gets for free.
        var padding = windowHeight / 2;

        using var ocrSession = ocrSettings is { Count: > 0 }
            ? await _ocr.CreateSessionAsync(ocrSettings, cancellationToken).ConfigureAwait(false)
            : await _ocr.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // The window-slide step splits genuinely wide gaps (label/value lists) unusually far
        // apart, so scanning always needs its own wordGapThreshold regardless of whatever the
        // user configured for the text-layout engine elsewhere — it wins over a user override.
        var mergedTextLayoutSettings = textLayoutSettings is { Count: > 0 }
            ? new Dictionary<string, object>(textLayoutSettings)
            : [];
        mergedTextLayoutSettings["wordGapThreshold"] = options.WordGapThresholdPercent;

        using var layoutSession = await _layout.CreateSessionAsync(mergedTextLayoutSettings, cancellationToken).ConfigureAwait(false);

        var tracked = new Dictionary<Guid, TrackedLine>();
        var committed = new List<TrackedLine>();
        var rotatedObservations = new RotatedTextClusterer.ObservationSet();

        for (var y = -padding; y + windowHeight <= page.Height + padding; y += options.Step)
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

            Accumulate(trustedLines, tracked, committed, options.MissingFrameGrace, page, inkThreshold, y, options.MarginMultiplier);

            if (outlierWords.Count > 0)
            {
                var offsetOutliers = new OffsetOcrResult(outlierWords, ocrResult.Confidence, new Vector2(0, y));
                rotatedObservations.Add(offsetOutliers.Words);
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
        return new ScanResult(Dedupe(committed), rotatedObservations.BuildClusters());
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
        float marginMultiplier)
    {
        var seenThisFrame = new HashSet<Guid>();
        foreach (var line in lines)
        {
            seenThisFrame.Add(line.Id);

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
        var ordered = committed.OrderBy(l => l.TopY).ToList();

        var deduped = new List<TrackedLine>();
        foreach (var line in ordered)
        {
            var text = line.Text.Trim();
            if (text.Length == 0)
                continue;

            if (deduped.Count > 0)
            {
                var prev = deduped[^1];
                var prevText = prev.Text.Trim();
                var samePosition = IsSamePosition(line, prev);
                var overlapsText = text.Contains(prevText, StringComparison.Ordinal)
                    || prevText.Contains(text, StringComparison.Ordinal);
                if (samePosition && (overlapsText || IsNearDuplicate(text, prevText)))
                {
                    var preferCurrent = text.Length != prevText.Length
                        ? text.Length > prevText.Length
                        : line.FrameCount > prev.FrameCount;
                    if (preferCurrent)
                        deduped[^1] = line;
                    continue;
                }
            }

            deduped.Add(line);
        }

        return deduped
            .Select(l => new ScannedLine(l.Text.Trim(), l.TopY, l.MinX, l.MaxX, l.Height, l.Words))
            .ToList();
    }

    /// <summary>
    /// True when two lines plausibly occupy the same physical spot on the page - close in Y
    /// (roughly one text line's height) AND overlapping in X by a meaningful fraction of the
    /// narrower one's width. Y-proximity alone isn't enough: two genuinely different lines (e.g.
    /// side-by-side columns) can sit at the same height without being the same line at all.
    /// </summary>
    private static bool IsSamePosition(TrackedLine a, TrackedLine b)
    {
        if (a.TopY - b.TopY >= 60) // roughly one text line's height
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
            InkDensityEstimator.MeasureDensity(page, w.Bounds.MinX, w.Bounds.MinY, w.Bounds.MaxX, w.Bounds.MaxY, inkThreshold)))
            .ToList();

    private sealed class TrackedLine
    {
        public required string Text { get; set; }
        public required float TopY { get; set; }
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float Height { get; set; }
        public List<ScannedWord> Words { get; set; } = [];
        public int MissingFrames { get; set; }

        /// <summary>How many frames this Id was actually seen in - used in Dedupe to break ties
        /// between two full-length near-duplicate readings left by an Id hop.</summary>
        public int FrameCount { get; set; }
    }
}
