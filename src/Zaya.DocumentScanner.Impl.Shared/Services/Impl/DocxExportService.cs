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
                if (!DocumentLineGrouping.TryDetectTwoColumns(paragraphLines, out var left, out var right, out var ambiguous))
                {
                    body.Append(BuildParagraph(paragraphLines, fontHalfPoints));
                    continue;
                }

                // A letterhead-style block (e.g. clinic info left, patient info right) — render as
                // a borderless two-cell table, with any line that didn't cleanly fit either column
                // kept in its own ordinary paragraph before/after the table, in original reading order.
                var tableTopY = Math.Min(left[0].TopY, right[0].TopY);
                foreach (var line in ambiguous.Where(l => l.TopY < tableTopY))
                    body.Append(BuildParagraph([line], fontHalfPoints));

                body.Append(BuildColumnsTable(left, right, page.WidthPx, fontHalfPoints));

                foreach (var line in ambiguous.Where(l => l.TopY >= tableTopY))
                    body.Append(BuildParagraph([line], fontHalfPoints));
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

    private static Paragraph BuildParagraph(IReadOnlyList<ScannedLine> lines, string fontHalfPoints)
    {
        var sentences = DocumentLineGrouping.JoinSentences(lines);
        var run = new Run(new RunProperties(new FontSize { Val = fontHalfPoints }));
        for (var i = 0; i < sentences.Count; i++)
        {
            if (i > 0)
                run.Append(new Break());
            run.Append(new Text(sentences[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        return new Paragraph(run);
    }

    private static Table BuildColumnsTable(
        List<ScannedLine> left, List<ScannedLine> right, int pageWidthPx, string fontHalfPoints)
    {
        var usableWidthTwips = A4WidthTwips - 2 * MarginTwips;

        // Split the table at the midpoint of the gap between the two blocks, scaled from source
        // pixels to document twips, so the columns keep roughly the same proportions as the scan.
        var splitX = (left.Max(l => l.MaxX) + right.Min(l => l.MinX)) / 2.0;
        var leftFraction = Math.Clamp(splitX / pageWidthPx, 0.15, 0.85);
        var leftWidthTwips = (int)Math.Round(leftFraction * usableWidthTwips);
        var rightWidthTwips = usableWidthTwips - leftWidthTwips;

        return new Table(
            new TableProperties(
                new TableWidth { Width = usableWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableBorders(
                    new TopBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new BottomBorder { Val = BorderValues.None },
                    new RightBorder { Val = BorderValues.None },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })),
            new TableGrid(
                new GridColumn { Width = leftWidthTwips.ToString() },
                new GridColumn { Width = rightWidthTwips.ToString() }),
            new TableRow(
                BuildCell(left, leftWidthTwips, fontHalfPoints),
                BuildCell(right, rightWidthTwips, fontHalfPoints)));
    }

    private static TableCell BuildCell(List<ScannedLine> lines, int widthTwips, string fontHalfPoints)
    {
        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa }));
        cell.Append(BuildParagraph(lines, fontHalfPoints));
        return cell;
    }
}
