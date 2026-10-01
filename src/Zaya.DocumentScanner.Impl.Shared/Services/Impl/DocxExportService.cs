using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Vml;
using DocumentFormat.OpenXml.Wordprocessing;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.Logging.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

[Log(LogLevel.Debug)]
public sealed class DocxExportService : IDocxExportService
{
    public void Export(IReadOnlyList<ScannedPage> pages, string path, DocxExportOptions? options = null)
    {
        options ??= new DocxExportOptions();
        var marginTwips = PageSizes.MarginTwipsFromMm(options.PageMarginMm);

        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());
        var shapeIdCounter = 1;

        for (var i = 0; i < pages.Count; i++)
        {
            // A page break before every source image but the first, so each scanned image becomes
            // its own physical page. This is a paragraph PROPERTY (pageBreakBefore), not a break
            // CHARACTER (<w:br w:type="page"/>) in its own paragraph - a break character still
            // occupies a position in the text flow, so when the previous page's content already
            // reaches the bottom margin, that character alone doesn't fit and pushes the whole
            // (otherwise empty) paragraph onto a page of its own, leaving a spurious blank page
            // before the next image's content. pageBreakBefore carries no content and no line of
            // its own to overflow - it's a layout hint, so it can't strand itself like that.
            if (i > 0)
                body.Append(new Paragraph(new ParagraphProperties(new PageBreakBefore())));

            AppendPageBody(body, pages[i].Lines, pages[i].RotatedClusters, pages[i].Metrics, options, marginTwips, ref shapeIdCounter);
        }

        var (pageWidthTwips, pageHeightTwips) = PageSizes.Get(options.PageSize);

        body.Append(new SectionProperties(
            new PageSize { Width = (UInt32Value)(uint)pageWidthTwips, Height = (UInt32Value)(uint)pageHeightTwips },
            new PageMargin
            {
                Top = marginTwips,
                Bottom = marginTwips,
                Left = (UInt32Value)(uint)marginTwips,
                Right = (UInt32Value)(uint)marginTwips,
            }));

        mainPart.Document.Save();
    }

    private static void AppendPageBody(
        Body body, IReadOnlyList<ScannedLine> lines, IReadOnlyList<RotatedTextCluster> rotatedClusters,
        PageMetrics page, DocxExportOptions options, int marginTwips, ref int shapeIdCounter)
    {
        var paragraphs = DocumentLineGrouping.GroupIntoParagraphs(lines);
        if (paragraphs.Count == 0)
        {
            body.Append(new Paragraph());
            AppendRotatedClusters(body, rotatedClusters, lines, 1.0, options, marginTwips, ref shapeIdCounter);
            return;
        }

        var pixelsPerPoint = PageLayoutEstimator.EstimatePixelsPerPoint(lines, options.PageSize, options.PageMarginMm);
        var scale = PageFitScaler.EstimateScale(lines, page, pixelsPerPoint, options);
        var leftMargin = PageLayoutEstimator.EstimateLeftMargin(lines);
        var rightMargin = PageLayoutEstimator.EstimateRightMargin(lines);
        var tolerance = PageLayoutEstimator.EstimateTypicalHeight(lines) * (options.LayoutTolerancePercent / 100f);
        var boldThreshold = PageLayoutEstimator.EstimateBoldDensityThreshold(lines);

        foreach (var paragraphLines in paragraphs)
        {
            if (!DocumentLineGrouping.TryDetectTwoColumns(paragraphLines, out var left, out var right, out var ambiguous))
            {
                AppendAlignedParagraphs(body, paragraphLines, pixelsPerPoint, scale, leftMargin, rightMargin, tolerance, boldThreshold, options);
                continue;
            }

            // A letterhead-style block (e.g. clinic info left, patient info right) — render as
            // a borderless two-cell table, with any line that didn't cleanly fit either column
            // kept in its own ordinary paragraph before/after the table, in original reading order.
            var tableTopY = Math.Min(left[0].TopY, right[0].TopY);
            foreach (var line in ambiguous.Where(l => l.TopY < tableTopY))
                AppendAlignedParagraphs(body, [line], pixelsPerPoint, scale, leftMargin, rightMargin, tolerance, boldThreshold, options);

            body.Append(BuildColumnsTable(
                left, right, page.WidthPx, pixelsPerPoint, scale, rightMargin, tolerance, boldThreshold, options, marginTwips));

            foreach (var line in ambiguous.Where(l => l.TopY >= tableTopY))
                AppendAlignedParagraphs(body, [line], pixelsPerPoint, scale, leftMargin, rightMargin, tolerance, boldThreshold, options);
        }

        AppendRotatedClusters(body, rotatedClusters, lines, pixelsPerPoint, options, marginTwips, ref shapeIdCounter);
    }

    /// <summary>
    /// Renders each rotated-text cluster (e.g. a round stamp's text - see RotatedTextClusterer) as
    /// its own free-floating, rotated VML text box, positioned near where it sat on the source
    /// page, instead of forcing it into the normal horizontal paragraph flow at zero rotation -
    /// which a genuinely rotated/curved source can't survive without losing content or becoming
    /// unreadable (see DocumentScanService's own notes on the word-dropping and curved-line-assembly
    /// attempts that were tried and abandoned before this).
    ///
    /// Uses legacy VML (w:pict/v:shape), not DrawingML, specifically because VML's rotation is a
    /// plain degrees value in the shape's own style string - no nested transform matrix to get
    /// wrong - and it's still fully supported by current Word. Position is estimated the same way
    /// EstimateLeftMargin anchors normal text: the cluster's source-page pixel position, relative
    /// to the first recognized text's own top-left (page content doesn't start at pixel (0,0) in
    /// the source photo), scaled to points and offset by the page's own configured margin.
    /// </summary>
    private static void AppendRotatedClusters(
        Body body, IReadOnlyList<RotatedTextCluster> clusters, IReadOnlyList<ScannedLine> lines,
        double pixelsPerPoint, DocxExportOptions options, int marginTwips, ref int shapeIdCounter)
    {
        if (clusters.Count == 0 || pixelsPerPoint <= 0)
            return;

        var marginPt = marginTwips / 20.0;
        var leftMarginPx = lines.Count > 0 ? PageLayoutEstimator.EstimateLeftMargin(lines) : 0f;
        var topMarginPx = lines.Count > 0 ? lines.Min(l => l.TopY) : 0f;

        var paragraph = new Paragraph();
        foreach (var cluster in clusters)
        {
            var widthPt = Math.Max(20.0, cluster.Width / pixelsPerPoint);
            var heightPt = Math.Max(10.0, cluster.Height / pixelsPerPoint);
            var leftPt = marginPt + (cluster.CenterX - cluster.Width / 2f - leftMarginPx) / pixelsPerPoint;
            var topPt = marginPt + (cluster.CenterY - cluster.Height / 2f - topMarginPx) / pixelsPerPoint;

            // Sized off the cluster's own height, same spirit as EstimateFontSizePoints for
            // ordinary text - a tall cluster (big stamp) gets bigger text than a small one.
            var fontSizePt = Math.Clamp(heightPt * 0.5, options.FontSizeMinPt, options.FontSizeMaxPt);
            var fontHalfPoints = ((int)Math.Round(fontSizePt * 2)).ToString();

            var style = string.Format(
                CultureInfo.InvariantCulture,
                "position:absolute;margin-left:{0:F2}pt;margin-top:{1:F2}pt;width:{2:F2}pt;height:{3:F2}pt;" +
                "rotation:{4:F1};mso-position-horizontal-relative:page;mso-position-vertical-relative:page;z-index:1",
                leftPt, topPt, widthPt, heightPt, cluster.AngleDegrees);

            var shape = new Shape
            {
                Id = $"ZayaRotated{shapeIdCounter++}",
                Type = "#_x0000_t202",
                Style = style,
                Filled = false,
                Stroked = false,
            };

            var textRun = new Run(
                new RunProperties(
                    new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily, ComplexScript = options.FontFamily },
                    new FontSize { Val = fontHalfPoints }));
            textRun.Append(new Text(cluster.Text) { Space = SpaceProcessingModeValues.Preserve });

            var textBox = new TextBox { Style = "mso-fit-shape-to-text:t", Inset = "0,0,0,0" };
            textBox.Append(new TextBoxContent(new Paragraph(textRun)));
            shape.Append(textBox);

            paragraph.Append(new Run(new Picture(shape)));
        }

        body.Append(paragraph);
    }

    /// <summary>
    /// Splits a Y-gap paragraph group into alignment-consistent runs (e.g. a centered heading
    /// followed by ordinary left-aligned body text) and appends one Word paragraph per run.
    /// </summary>
    private static void AppendAlignedParagraphs(
        Body body, IReadOnlyList<ScannedLine> groupLines, double pixelsPerPoint, double scale,
        float leftMargin, float rightMargin, float tolerance, double boldThreshold, DocxExportOptions options)
    {
        foreach (var (alignment, units) in DocumentLineGrouping.SplitByAlignment(groupLines, leftMargin, rightMargin, tolerance))
            body.Append(BuildParagraph(units, pixelsPerPoint, scale, alignment, boldThreshold, options));
    }

    private static Paragraph BuildParagraph(
        List<DocumentLineGrouping.SentenceUnit> units, double pixelsPerPoint, double scale, LineAlignment alignment,
        double boldThreshold, DocxExportOptions options)
    {
        var allLines = units.SelectMany(u => u.Lines).ToList();
        var baseFontSizePt = PageLayoutEstimator.EstimateFontSizePoints(allLines, pixelsPerPoint, options.FontSizeMinPt, options.FontSizeMaxPt);
        var targetFontSizePt = Math.Max(options.FontSizeMinPt, baseFontSizePt * scale);
        var fontHalfPoints = ((int)Math.Round(targetFontSizePt * 2)).ToString();

        // Without an explicit spacing, Word's own default paragraph spacing (space-after ~8pt,
        // ~1.08x line height) applies to every one of the dozens of paragraphs this produces — a
        // source form set with no such spacing then needs noticeably more pages once reproduced
        // with it.
        //
        // When PageFitScaler found a correction is needed (scale < 1), the line height is instead
        // pinned to an exact value computed from the true, unrounded target font size - a small
        // correction (a percent or two) would otherwise vanish once fontHalfPoints rounds back to
        // the same half-point it started at, especially for body text already near FontSizeMinPt,
        // since w:sz only has half-point resolution but w:spacing's exact line height does not.
        var spacing = scale < 0.999
            ? new SpacingBetweenLines
            {
                Before = "0",
                After = "0",
                Line = Math.Max(1, (int)Math.Round(targetFontSizePt * PageFitScaler.LineSpacingFactor * 20)).ToString(),
                LineRule = LineSpacingRuleValues.Exact,
            }
            : new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto };

        var paragraphProperties = new ParagraphProperties(spacing);
        if (alignment != LineAlignment.Left)
        {
            paragraphProperties.Append(new Justification
            {
                Val = alignment == LineAlignment.Center ? JustificationValues.Center : JustificationValues.Right,
            });
        }

        var paragraph = new Paragraph(paragraphProperties);

        // Flatten to one entry per original word, each carrying whether a hard line break
        // (<w:br/>) precedes it — see DocumentLineGrouping.FlattenWords for when that is (start of
        // a new sentence unit, or a word gap so wide within its own source line that gluing it to
        // the previous word would read as one run-on phrase or compound word).
        //
        // No smoothing here: this document bolds individual words deliberately (a field's label
        // but not its value, e.g. "Пол:" bold / "Мужской" not) — a real, per-word distinction, not
        // noise to be blurred toward its neighbors. Ink density still misreads some words on their
        // own (an open/round-heavy word can measure lower than a denser one at the same weight),
        // but "smooth toward neighbors" was actively wrong for the genuine label/value case and
        // didn't reliably fix the real noise either, so the raw per-word reading is used as-is.
        var words = DocumentLineGrouping.FlattenWords(units);

        Run? run = null;
        bool? runIsBold = null;
        var pendingSpace = false;

        foreach (var (word, breakBefore) in words)
        {
            var isBold = options.BoldDetectionEnabled && word.InkDensity >= boldThreshold;
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

            run.Append(new Text((pendingSpace ? " " : "") + word.Text) { Space = SpaceProcessingModeValues.Preserve });
            pendingSpace = true;
        }

        return paragraph;
    }

    private static Table BuildColumnsTable(
        List<ScannedLine> left, List<ScannedLine> right, int pageWidthPx, double pixelsPerPoint, double scale,
        float rightMargin, float rightMarginTolerance, double boldThreshold, DocxExportOptions options, int marginTwips)
    {
        var (pageWidthTwips, _) = PageSizes.Get(options.PageSize);
        var usableWidthTwips = pageWidthTwips - 2 * marginTwips;

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
                BuildCell(left, leftWidthTwips, pixelsPerPoint, scale, rightMargin, rightMarginTolerance, boldThreshold, options),
                BuildCell(right, rightWidthTwips, pixelsPerPoint, scale, rightMargin, rightMarginTolerance, boldThreshold, options)));
    }

    private static TableCell BuildCell(
        List<ScannedLine> lines, int widthTwips, double pixelsPerPoint, double scale, float rightMargin, float rightMarginTolerance,
        double boldThreshold, DocxExportOptions options)
    {
        var units = DocumentLineGrouping.SplitIntoSentenceUnits(lines, rightMargin, rightMarginTolerance);
        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa }));
        cell.Append(BuildParagraph(units, pixelsPerPoint, scale, LineAlignment.Left, boldThreshold, options));
        return cell;
    }
}
