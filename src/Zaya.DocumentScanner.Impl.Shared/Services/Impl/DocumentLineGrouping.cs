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
    /// a period and the next line starts with a lowercase letter, the scan's line width — not
    /// the source document — is what broke the sentence there, so the two are joined with a
    /// space instead of kept as a hard break (e.g. a label/value block like "Пол: Мужской" /
    /// "Дата рождения: ..." stays split, since each starts with a capital).
    /// </summary>
    public static List<string> JoinSentences(IReadOnlyList<ScannedLine> group)
    {
        var result = new List<string>();
        var current = group[0].Text;

        for (var i = 1; i < group.Count; i++)
        {
            var prevEndsSentence = current.TrimEnd().EndsWith('.');
            var nextStartsLower = TryGetFirstLetter(group[i].Text, out var firstLetter) && char.IsLower(firstLetter);

            if (!prevEndsSentence && nextStartsLower)
            {
                current = current.TrimEnd() + " " + group[i].Text.TrimStart();
            }
            else
            {
                result.Add(current);
                current = group[i].Text;
            }
        }

        result.Add(current);
        return result;
    }

    /// <summary>
    /// Classifies a paragraph's alignment by comparing its own left/right extent against the
    /// document's overall text margins (a low/high percentile of every line's edges, robust to
    /// the occasional stray word). A block touching only the left margin is Left, touching only
    /// the right margin is Right, and one floating clear of both with roughly equal gaps on each
    /// side is Center.
    /// </summary>
    public static LineAlignment ClassifyAlignment(
        IReadOnlyList<ScannedLine> group,
        float documentLeftMargin,
        float documentRightMargin,
        float tolerance)
    {
        var groupMinX = group.Min(l => l.MinX);
        var groupMaxX = group.Max(l => l.MaxX);

        var leftGap = groupMinX - documentLeftMargin;
        var rightGap = documentRightMargin - groupMaxX;

        if (leftGap <= tolerance)
            return LineAlignment.Left;

        if (rightGap <= tolerance)
            return LineAlignment.Right;

        if (Math.Abs(leftGap - rightGap) <= tolerance)
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
