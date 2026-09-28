using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

public sealed class DocxExportService : IDocxExportService
{
    public void Export(IReadOnlyList<ScannedLine> lines, string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        var paragraphs = DocumentLineGrouping.GroupIntoParagraphs(lines);
        if (paragraphs.Count == 0)
        {
            body.Append(new Paragraph());
        }

        foreach (var paragraphLines in paragraphs)
        {
            var run = new Run();
            for (var i = 0; i < paragraphLines.Count; i++)
            {
                if (i > 0)
                    run.Append(new Break());
                run.Append(new Text(paragraphLines[i].Text) { Space = SpaceProcessingModeValues.Preserve });
            }

            body.Append(new Paragraph(run));
        }

        mainPart.Document.Save();
    }
}
