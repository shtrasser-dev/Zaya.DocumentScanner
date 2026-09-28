using System.Text;
using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>Joins scanned lines into displayable text, reinserting paragraph breaks by gap size.</summary>
public static class DocumentTextFormatter
{
    public static string ToPlainText(IReadOnlyList<ScannedLine> lines)
    {
        if (lines.Count == 0)
            return string.Empty;

        var gaps = lines.Zip(lines.Skip(1), (a, b) => b.TopY - a.TopY).Where(g => g > 0).ToList();
        var typicalGap = gaps.Count > 0 ? MathHelpers.Median(gaps) : 40f;

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                var gap = lines[i].TopY - lines[i - 1].TopY;
                sb.Append(gap > typicalGap * 1.6 ? "\n\n" : "\n");
            }

            sb.Append(lines[i].Text);
        }

        return sb.ToString();
    }
}
