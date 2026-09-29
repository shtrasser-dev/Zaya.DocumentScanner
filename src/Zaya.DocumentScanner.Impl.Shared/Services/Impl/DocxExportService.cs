using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

public sealed class DocxExportService : IDocxExportService
{
    // A4 in twips (1/20 pt): 210x297mm. Word's own default margins (1 inch = 1440 twips).
    private const int A4WidthTwips = 11906;
    private const int A4HeightTwips = 16838;
    private const int MarginTwips = 1440;

    public void Export(IReadOnlyList<ScannedLine> lines, PageMetrics page, string path)
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
        else
        {
            var fontHalfPoints = (Math.Round(PageLayoutEstimator.EstimateFontSizePoints(lines, page.HeightPx) * 2))
                .ToString();

            foreach (var paragraphLines in paragraphs)
            {
                var sentences = DocumentLineGrouping.JoinSentences(paragraphLines);

                var run = new Run(new RunProperties(new FontSize { Val = fontHalfPoints }));
                for (var i = 0; i < sentences.Count; i++)
                {
                    if (i > 0)
                        run.Append(new Break());
                    run.Append(new Text(sentences[i]) { Space = SpaceProcessingModeValues.Preserve });
                }

                body.Append(new Paragraph(run));
            }
        }

        body.Append(new SectionProperties(
            new PageSize { Width = A4WidthTwips, Height = A4HeightTwips },
            new PageMargin
            {
                Top = MarginTwips,
                Bottom = MarginTwips,
                Left = (UInt32Value)(uint)MarginTwips,
                Right = (UInt32Value)(uint)MarginTwips,
            }));

        mainPart.Document.Save();
    }
}
