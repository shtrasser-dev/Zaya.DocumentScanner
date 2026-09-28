using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>Joins scanned lines into displayable plain text, one blank line between paragraphs.</summary>
public static class DocumentTextFormatter
{
    public static string ToPlainText(IReadOnlyList<ScannedLine> lines)
    {
        var paragraphs = DocumentLineGrouping.GroupIntoParagraphs(lines);
        return string.Join(
            "\n\n",
            paragraphs.Select(p => string.Join("\n", p.Select(l => l.Text))));
    }
}
