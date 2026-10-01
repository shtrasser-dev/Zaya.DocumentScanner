using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Groups scanned lines into paragraphs by vertical gap: a gap much larger than the typical
/// (median) line-to-line spacing marks a paragraph break. Shared by the plain-text formatter
/// and the DOCX exporter so both reflect the same paragraph boundaries.
/// </summary>
public static class DocumentLineGrouping
{
    public static List<List<ScannedLine>> GroupIntoParagraphs(IReadOnlyList<ScannedLine> lines)
    {
        if (lines.Count == 0)
            return [];

        var typicalGap = TypicalGap(lines);

        var groups = new List<List<ScannedLine>> { new() { lines[0] } };
        for (var i = 1; i < lines.Count; i++)
        {
            var gap = lines[i].TopY - lines[i - 1].TopY;
            if (gap > typicalGap * 1.6)
                groups.Add([]);

            groups[^1].Add(lines[i]);
        }

        return groups;
    }

    /// <summary>One or more scanned lines merged into a single flowing sentence (see <see cref="SplitIntoSentenceUnits"/>).</summary>
    public sealed record SentenceUnit(string Text, IReadOnlyList<ScannedLine> Lines);

    /// <summary>
    /// Merges word-wrapped sentence fragments within a paragraph: when a line doesn't end with
    /// a period, the next line starts with a lowercase letter, AND the line actually runs out to
    /// the page's right margin, the scan's line width — not the source document — is what broke
    /// the sentence there, so the two are joined with a space instead of kept as a hard break
    /// (e.g. a label/value block like "Пол: Мужской" / "Дата рождения: ..." stays split, since
    /// each starts with a capital). The margin check matters on its own too: a short line — e.g.
    /// each line of a bulleted list, which often starts with a lowercase word right after the
    /// bullet and has no reason to end in a period — must not be merged into its neighbor just
    /// because it falls short of a full sentence; only a line that actually reached the margin
    /// was plausibly cut off by the page width rather than ending on its own.
    /// </summary>
    public static List<SentenceUnit> SplitIntoSentenceUnits(
        IReadOnlyList<ScannedLine> group, float rightMargin, float rightMarginTolerance)
    {
        var result = new List<SentenceUnit>();
        var currentText = group[0].Text;
        var currentLines = new List<ScannedLine> { group[0] };
        var lastLine = group[0];

        for (var i = 1; i < group.Count; i++)
        {
            var prevEndsSentence = currentText.TrimEnd().EndsWith('.');
            var nextStartsLower = TryGetFirstLetter(group[i].Text, out var firstLetter) && char.IsLower(firstLetter);
            var prevReachesRightMargin = lastLine.MaxX >= rightMargin - rightMarginTolerance;

            if (!prevEndsSentence && nextStartsLower && prevReachesRightMargin)
            {
                currentText = currentText.TrimEnd() + " " + group[i].Text.TrimStart();
                currentLines.Add(group[i]);
            }
            else
            {
                result.Add(new SentenceUnit(currentText, currentLines));
                currentText = group[i].Text;
                currentLines = [group[i]];
            }

            lastLine = group[i];
        }

        result.Add(new SentenceUnit(currentText, currentLines));
        return result;
    }

    /// <summary>Text-only view of <see cref="SplitIntoSentenceUnits"/>, for plain-text output which has no use for alignment.</summary>
    public static List<string> JoinSentences(
        IReadOnlyList<ScannedLine> group, float rightMargin, float rightMarginTolerance)
        => SplitIntoSentenceUnits(group, rightMargin, rightMarginTolerance).Select(u => u.Text).ToList();

    /// <summary>One word from a flattened sentence unit, and whether a hard break belongs before it.</summary>
    public readonly record struct FlattenedWord(ScannedWord Word, bool BreakBefore);

    /// <summary>
    /// A gap between two consecutive words of the SAME scanned line this many times that line's
    /// own text height almost certainly isn't a real word gap - it's the signature of
    /// LineAssembler bridging two physically unrelated chunks into one line (e.g. a letterhead's
    /// side-by-side blocks, joined across the gap between them because WordGapThreshold is
    /// widened to 150% for ordinary label/value lines). There's no reliable way to tell this
    /// apart from a genuinely wide gap by column-boundary detection - column/paragraph detection
    /// always has a real error rate - but refusing to silently fuse the two chunks into one
    /// run-on phrase is a much safer, narrower call: at worst this breaks a line that didn't need
    /// it (an extra wrap), never a glued, unreadable compound word.
    /// </summary>
    private const float WordGapOutlierHeightMultiplier = 3f;

    /// <summary>
    /// Flattens sentence units into one entry per original word, each carrying whether a hard
    /// break belongs before it - at the start of a new sentence unit (first word only), or before
    /// a word that follows a gap so wide inside its own source line that it almost certainly isn't
    /// really adjacent to the previous word (see <see cref="WordGapOutlierHeightMultiplier"/>).
    /// Shared by the DOCX exporter and the live preview so both split the same way.
    /// </summary>
    public static List<FlattenedWord> FlattenWords(IReadOnlyList<SentenceUnit> units)
    {
        var result = new List<FlattenedWord>();
        foreach (var unit in units)
        {
            var isFirstWordOfUnit = true;
            foreach (var line in unit.Lines)
            {
                ScannedWord? prevWord = null;
                foreach (var word in line.Words)
                {
                    var height = line.Height > 0 ? line.Height : 20f;
                    var isGapOutlier = prevWord is not null
                        && word.MinX - prevWord.MaxX > height * WordGapOutlierHeightMultiplier;
                    var breakBefore = (isFirstWordOfUnit && result.Count > 0) || isGapOutlier;
                    result.Add(new FlattenedWord(word, breakBefore));
                    isFirstWordOfUnit = false;
                    prevWord = word;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Splits a paragraph-sized group into runs of consecutive sentence units that share the same
    /// alignment (see <see cref="ClassifyAlignment"/>), each destined for its own Word paragraph —
    /// a centered one-line heading followed by ordinary left-aligned body text becomes two runs,
    /// rather than one paragraph whose alignment is decided by the group as a whole (which would
    /// almost always come out Left, since the group's combined extent touches the left margin).
    /// </summary>
    public static List<(LineAlignment Alignment, List<SentenceUnit> Units)> SplitByAlignment(
        IReadOnlyList<ScannedLine> group, float leftMargin, float rightMargin, float tolerance)
    {
        var units = SplitIntoSentenceUnits(group, rightMargin, tolerance);
        var runs = new List<(LineAlignment Alignment, List<SentenceUnit> Units)>();
        foreach (var unit in units)
        {
            var alignment = ClassifyAlignment(unit.Lines, leftMargin, rightMargin, tolerance);
            if (runs.Count > 0 && runs[^1].Alignment == alignment)
                runs[^1].Units.Add(unit);
            else
                runs.Add((alignment, [unit]));
        }

        return runs;
    }

    /// <summary>
    /// Detects whether a paragraph-sized group of lines is really two independent blocks sitting
    /// side by side (e.g. a letterhead: clinic info on the left, patient info on the right) rather
    /// than one flowing column of text — requires at least two lines cleanly on each side with
    /// non-overlapping horizontal extents. A line that straddles both sides (e.g. one that bridges
    /// a genuine column gap during word-merge) is reported separately in
    /// <paramref name="ambiguous"/> instead of forcing it into either column.
    /// </summary>
    public static bool TryDetectTwoColumns(
        IReadOnlyList<ScannedLine> group,
        out List<ScannedLine> left,
        out List<ScannedLine> right,
        out List<ScannedLine> ambiguous)
    {
        left = [];
        right = [];
        ambiguous = [];
        if (group.Count < 4)
            return false;

        var order = Enumerable.Range(0, group.Count).OrderBy(i => group[i].MinX).ToList();
        var minGap = MathHelpers.Median(group.Select(l => l.Height).Where(h => h > 0).ToList()) * 0.75f;

        var splitPos = -1;
        var bestGap = 0f;
        for (var i = 1; i < order.Count; i++)
        {
            var gap = group[order[i]].MinX - group[order[i - 1]].MinX;
            if (gap <= bestGap)
                continue;
            bestGap = gap;
            splitPos = i;
        }

        if (splitPos < 0 || bestGap < minGap)
            return false;

        var leftIdx = order.Take(splitPos).ToHashSet();
        var rightIdx = order.Skip(splitPos).ToHashSet();
        var ambiguousIdx = new HashSet<int>();

        // A line whose own extent steps into the other side's territory isn't cleanly on either
        // side — pull it out and re-check. The right-side check must run against the extent left
        // AFTER removing that pass's left-side outliers (not before), otherwise one wide outlier
        // still sitting in leftIdx inflates leftMaxX and wrongly flags every right-side line too.
        for (var pass = 0; pass < 3; pass++)
        {
            if (leftIdx.Count == 0 || rightIdx.Count == 0)
                break;

            var rightMinX = rightIdx.Min(i => group[i].MinX);
            var movedLeft = leftIdx.Where(i => group[i].MaxX > rightMinX).ToList();
            foreach (var i in movedLeft) { leftIdx.Remove(i); ambiguousIdx.Add(i); }

            if (leftIdx.Count == 0)
                break;

            var leftMaxX = leftIdx.Max(i => group[i].MaxX);
            var movedRight = rightIdx.Where(i => group[i].MinX < leftMaxX).ToList();
            foreach (var i in movedRight) { rightIdx.Remove(i); ambiguousIdx.Add(i); }

            if (movedLeft.Count == 0 && movedRight.Count == 0)
                break;
        }

        if (leftIdx.Count < 2 || rightIdx.Count < 2)
        {
            left = [];
            right = [];
            ambiguous = [];
            return false;
        }

        left = Enumerable.Range(0, group.Count).Where(leftIdx.Contains).Select(i => group[i]).ToList();
        right = Enumerable.Range(0, group.Count).Where(rightIdx.Contains).Select(i => group[i]).ToList();
        ambiguous = Enumerable.Range(0, group.Count).Where(ambiguousIdx.Contains).Select(i => group[i]).ToList();
        return true;
    }

    /// <summary>
    /// Classifies one sentence unit's alignment (usually a single line, occasionally several
    /// merged by word-wrap — see <see cref="SplitIntoSentenceUnits"/>) by comparing its own extent
    /// against the document's overall text margins. Left/Right when it touches the corresponding
    /// margin; Center only when it sits clear of BOTH margins with its own midpoint close to the
    /// document's midpoint — not just "closer to center than to either edge", since that's a much
    /// weaker (and easily false-positive-prone) test.
    /// </summary>
    public static LineAlignment ClassifyAlignment(
        IReadOnlyList<ScannedLine> unit, float documentLeftMargin, float documentRightMargin, float tolerance)
    {
        var minX = unit.Min(l => l.MinX);
        var maxX = unit.Max(l => l.MaxX);
        var leftGap = minX - documentLeftMargin;
        var rightGap = documentRightMargin - maxX;

        if (leftGap <= tolerance)
            return LineAlignment.Left;

        if (rightGap <= tolerance)
            return LineAlignment.Right;

        var center = (minX + maxX) / 2f;
        var documentCenter = (documentLeftMargin + documentRightMargin) / 2f;
        if (Math.Abs(center - documentCenter) <= tolerance)
            return LineAlignment.Center;

        return LineAlignment.Left;
    }

    public static float TypicalGap(IReadOnlyList<ScannedLine> lines)
    {
        var gaps = lines.Zip(lines.Skip(1), (a, b) => b.TopY - a.TopY).Where(g => g > 0).ToList();
        return gaps.Count > 0 ? MathHelpers.Median(gaps) : 40f;
    }

    private static bool TryGetFirstLetter(string text, out char letter)
    {
        foreach (var c in text)
        {
            if (char.IsLetter(c))
            {
                letter = c;
                return true;
            }
        }

        letter = default;
        return false;
    }
}
