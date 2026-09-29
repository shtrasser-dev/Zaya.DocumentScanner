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
    public static List<string> JoinSentences(
        IReadOnlyList<ScannedLine> group, float rightMargin, float rightMarginTolerance)
    {
        var result = new List<string>();
        var current = group[0].Text;
        var lastLine = group[0];

        for (var i = 1; i < group.Count; i++)
        {
            var prevEndsSentence = current.TrimEnd().EndsWith('.');
            var nextStartsLower = TryGetFirstLetter(group[i].Text, out var firstLetter) && char.IsLower(firstLetter);
            var prevReachesRightMargin = lastLine.MaxX >= rightMargin - rightMarginTolerance;

            if (!prevEndsSentence && nextStartsLower && prevReachesRightMargin)
            {
                current = current.TrimEnd() + " " + group[i].Text.TrimStart();
            }
            else
            {
                result.Add(current);
                current = group[i].Text;
            }

            lastLine = group[i];
        }

        result.Add(current);
        return result;
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
