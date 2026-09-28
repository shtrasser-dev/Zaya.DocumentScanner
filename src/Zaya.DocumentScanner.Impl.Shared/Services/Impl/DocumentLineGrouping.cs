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

        var gaps = lines.Zip(lines.Skip(1), (a, b) => b.TopY - a.TopY).Where(g => g > 0).ToList();
        var typicalGap = gaps.Count > 0 ? MathHelpers.Median(gaps) : 40f;

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
}
