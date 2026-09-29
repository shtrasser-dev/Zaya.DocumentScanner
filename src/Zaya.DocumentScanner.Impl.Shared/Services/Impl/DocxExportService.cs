using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

public sealed class DocxExportService : IDocxExportService
{
    // Twips (1/20 pt). Word's own default margins (1 inch = 1440 twips).
    private const int A4WidthTwips = 11906;
    private const int A4HeightTwips = 16838;
    private const int LetterWidthTwips = 12240;
    private const int LetterHeightTwips = 15840;
    private const int MarginTwips = 1440;

    public void Export(IReadOnlyList<ScannedLine> lines, PageMetrics page, string path, DocxExportOptions? options = null)
    {
        options ??= new DocxExportOptions();

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
            var leftMargin = PageLayoutEstimator.EstimateLeftMargin(lines);
            var rightMargin = PageLayoutEstimator.EstimateRightMargin(lines);
            var tolerance = PageLayoutEstimator.EstimateTypicalHeight(lines) * (options.LayoutTolerancePercent / 100f);
            var boldThreshold = PageLayoutEstimator.EstimateBoldDensityThreshold(lines);

            foreach (var paragraphLines in paragraphs)
            {
                if (!DocumentLineGrouping.TryDetectTwoColumns(paragraphLines, out var left, out var right, out var ambiguous))
                {
                    AppendAlignedParagraphs(body, paragraphLines, page.HeightPx, leftMargin, rightMargin, tolerance, boldThreshold, options);
                    continue;
                }

                // A letterhead-style block (e.g. clinic info left, patient info right) — render as
                // a borderless two-cell table, with any line that didn't cleanly fit either column
                // kept in its own ordinary paragraph before/after the table, in original reading order.
                var tableTopY = Math.Min(left[0].TopY, right[0].TopY);
                foreach (var line in ambiguous.Where(l => l.TopY < tableTopY))
                    AppendAlignedParagraphs(body, [line], page.HeightPx, leftMargin, rightMargin, tolerance, boldThreshold, options);

                body.Append(BuildColumnsTable(left, right, page.WidthPx, page.HeightPx, rightMargin, tolerance, boldThreshold, options));

                foreach (var line in ambiguous.Where(l => l.TopY >= tableTopY))
                    AppendAlignedParagraphs(body, [line], page.HeightPx, leftMargin, rightMargin, tolerance, boldThreshold, options);
            }
        }

        var (pageWidthTwips, pageHeightTwips) = options.PageSize == SettingsConstants.PageSizeLetter
            ? (LetterWidthTwips, LetterHeightTwips)
            : (A4WidthTwips, A4HeightTwips);

        body.Append(new SectionProperties(
            new PageSize { Width = (UInt32Value)(uint)pageWidthTwips, Height = (UInt32Value)(uint)pageHeightTwips },
            new PageMargin
            {
                Top = MarginTwips,
                Bottom = MarginTwips,
                Left = (UInt32Value)(uint)MarginTwips,
                Right = (UInt32Value)(uint)MarginTwips,
            }));

        mainPart.Document.Save();
    }

    /// <summary>
    /// Splits a Y-gap paragraph group into alignment-consistent runs (e.g. a centered heading
    /// followed by ordinary left-aligned body text) and appends one Word paragraph per run.
    /// </summary>
    private static void AppendAlignedParagraphs(
        Body body, IReadOnlyList<ScannedLine> groupLines, int pageHeightPx,
        float leftMargin, float rightMargin, float tolerance, double boldThreshold, DocxExportOptions options)
    {
        foreach (var (alignment, units) in DocumentLineGrouping.SplitByAlignment(groupLines, leftMargin, rightMargin, tolerance))
            body.Append(BuildParagraph(units, pageHeightPx, alignment, boldThreshold, options));
    }

    private static Paragraph BuildParagraph(
        List<DocumentLineGrouping.SentenceUnit> units, int pageHeightPx, LineAlignment alignment,
        double boldThreshold, DocxExportOptions options)
    {
        var allLines = units.SelectMany(u => u.Lines).ToList();
        var fontHalfPoints = (Math.Round(
                PageLayoutEstimator.EstimateFontSizePoints(allLines, pageHeightPx, options.FontSizeMinPt, options.FontSizeMaxPt) * 2))
            .ToString();

        var paragraphProperties = new ParagraphProperties(
            // Without this, Word's own default paragraph spacing (space-after ~8pt, ~1.08x line
            // height) applies to every one of the dozens of paragraphs this produces — a source
            // form set with no such spacing then needs noticeably more pages once reproduced with it.
            new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });
        if (alignment != LineAlignment.Left)
        {
            paragraphProperties.Append(new Justification
            {
                Val = alignment == LineAlignment.Center ? JustificationValues.Center : JustificationValues.Right,
            });
        }

        var paragraph = new Paragraph(paragraphProperties);

        // Flatten to one entry per original word, each carrying whether a hard line break
        // (<w:br/>) precedes it. A break belongs only at the start of a new sentence unit — lines
        // merged together by word-wrap within one unit are joined by a plain space instead, same
        // as everywhere else in this pipeline that already draws that distinction.
        //
        // No smoothing here: this document bolds individual words deliberately (a field's label
        // but not its value, e.g. "Пол:" bold / "Мужской" not) — a real, per-word distinction, not
        // noise to be blurred toward its neighbors. Ink density still misreads some words on their
        // own (an open/round-heavy word can measure lower than a denser one at the same weight),
        // but "smooth toward neighbors" was actively wrong for the genuine label/value case and
        // didn't reliably fix the real noise either, so the raw per-word reading is used as-is.
        var words = new List<(string Text, bool IsBold, bool BreakBefore)>();
        foreach (var unit in units)
        {
            var isFirstWordOfUnit = true;
            foreach (var line in unit.Lines)
            {
                foreach (var word in line.Words)
                {
                    var isBold = options.BoldDetectionEnabled && word.InkDensity >= boldThreshold;
                    words.Add((word.Text, isBold, isFirstWordOfUnit && words.Count > 0));
                    isFirstWordOfUnit = false;
                }
            }
        }

        Run? run = null;
        bool? runIsBold = null;
        var pendingSpace = false;

        foreach (var (text, isBold, breakBefore) in words)
        {
            if (run is null || isBold != runIsBold)
            {
                var runProperties = isBold
                    ? new RunProperties(new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily, ComplexScript = options.FontFamily }, new Bold(), new FontSize { Val = fontHalfPoints })
                    : new RunProperties(new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily, ComplexScript = options.FontFamily }, new FontSize { Val = fontHalfPoints });
                run = new Run(runProperties);
                paragraph.Append(run);
                runIsBold = isBold;
            }

            if (breakBefore)
            {
                run.Append(new Break());
                pendingSpace = false;
            }

            run.Append(new Text((pendingSpace ? " " : "") + text) { Space = SpaceProcessingModeValues.Preserve });
            pendingSpace = true;
        }

        return paragraph;
    }

    private static Table BuildColumnsTable(
        List<ScannedLine> left, List<ScannedLine> right, int pageWidthPx, int pageHeightPx,
        float rightMargin, float rightMarginTolerance, double boldThreshold, DocxExportOptions options)
    {
        var pageWidthTwips = options.PageSize == SettingsConstants.PageSizeLetter ? LetterWidthTwips : A4WidthTwips;
        var usableWidthTwips = pageWidthTwips - 2 * MarginTwips;

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
                BuildCell(left, leftWidthTwips, pageHeightPx, rightMargin, rightMarginTolerance, boldThreshold, options),
                BuildCell(right, rightWidthTwips, pageHeightPx, rightMargin, rightMarginTolerance, boldThreshold, options)));
    }

    private static TableCell BuildCell(
        List<ScannedLine> lines, int widthTwips, int pageHeightPx, float rightMargin, float rightMarginTolerance,
        double boldThreshold, DocxExportOptions options)
    {
        var units = DocumentLineGrouping.SplitIntoSentenceUnits(lines, rightMargin, rightMarginTolerance);
        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa }));
        cell.Append(BuildParagraph(units, pageHeightPx, LineAlignment.Left, boldThreshold, options));
        return cell;
    }
}
