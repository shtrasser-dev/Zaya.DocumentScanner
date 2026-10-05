using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Vml;
using A = DocumentFormat.OpenXml.Drawing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;
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
        // Rotated text boxes are DrawingML word-processing shapes (see AppendRotatedClusters),
        // wrapped in mc:AlternateContent whose Choice requires the "wps" prefix - it has to be
        // declared on the root for Word to resolve that requirement.
        mainPart.Document.AddNamespaceDeclaration("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
        mainPart.Document.AddNamespaceDeclaration("wp", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing");
        mainPart.Document.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        mainPart.Document.AddNamespaceDeclaration("wps", "http://schemas.microsoft.com/office/word/2010/wordprocessingShape");
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
        var fontSizes = FontSizeEstimator.Estimate(lines, pixelsPerPoint, options.FontSizeMinPt, options.FontSizeMaxPt);
        var scale = PageFitScaler.EstimateScale(lines, page, fontSizes, options);
        var leftMargin = PageLayoutEstimator.EstimateLeftMargin(lines);
        var rightMargin = PageLayoutEstimator.EstimateRightMargin(lines);
        var tolerance = PageLayoutEstimator.EstimateTypicalHeight(lines) * (options.LayoutTolerancePercent / 100f);
        var boldThreshold = PageLayoutEstimator.EstimateBoldDensityThreshold(lines);

        // A letterhead-style or table stretch (e.g. labels | values | address box | clinic block)
        // renders as a borderless row/column table; any line that didn't cleanly fit one column
        // stays an ordinary paragraph at its own place in reading order (see SplitIntoBlocks).
        foreach (var block in DocumentLineGrouping.BuildBlocks(paragraphs))
        {
            if (block.IsTable)
            {
                body.Append(BuildTable(block.Table!, fontSizes, scale, rightMargin, tolerance, boldThreshold, options, marginTwips));
            }
            else
            {
                AppendAlignedParagraphs(body, block.Lines, fontSizes, scale, leftMargin, rightMargin, tolerance, boldThreshold, options);
            }
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
    /// The box is a DrawingML word-processing shape (wps:wsp, rotation in a:xfrm) with the old VML
    /// text box (v:shape, rotation:N in its style) as the mc:AlternateContent fallback - the same
    /// pairing Word itself writes. VML alone isn't enough: Word rotates a legacy VML text box's
    /// outline but always draws its text horizontally, which is exactly what a real export showed;
    /// DrawingML text boxes rotate their text with the shape. Position is estimated the same way
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
            // The font size has to come from an individual word's own height, not the cluster's
            // overall bounding box height - that reflects how far the words are spread around
            // (e.g. a stamp's diameter), not any single glyph's size. Using it directly earlier
            // produced font sizes several times too large for the box.
            var fontSizePt = Math.Clamp(cluster.TypicalWordHeightPx / pixelsPerPoint, options.FontSizeMinPt, options.FontSizeMaxPt);
            var fontHalfPoints = ((int)Math.Round(fontSizePt * 2)).ToString();

            var leftPt = marginPt + (cluster.CenterX - cluster.Width / 2f - leftMarginPx) / pixelsPerPoint;
            var topPt = marginPt + (cluster.CenterY - cluster.Height / 2f - topMarginPx) / pixelsPerPoint;

            // Start from the cluster's own spatial footprint (keeps the block roughly where/how
            // big the stamp itself was), but that footprint is about where the circle's words
            // ARE, not how much room their text needs once laid out as ordinary wrapped lines -
            // those can be very different for text that was originally arranged in a circle. Grow
            // the box height (not width - keep it visually compact) if a rough estimate says the
            // text wouldn't fit, computed here rather than left to Word's own auto-fit, so the
            // DrawingML box and its VML fallback get the same, predictable size.
            //
            // A little slack over the measured width: the export font is rarely the source's, and a
            // straight stamp's lines (sized to the source text exactly) would otherwise re-wrap.
            var widthPt = Math.Max(30.0, cluster.Width / pixelsPerPoint * 1.15);
            var avgCharWidthPt = fontSizePt * 0.55;
            var charsPerLine = Math.Max(1, (int)(widthPt / avgCharWidthPt));
            var linesNeeded = Math.Max(1, cluster.Text.Split('\n').Sum(l => (int)Math.Ceiling((double)Math.Max(1, l.Length) / charsPerLine)));
            var estimatedHeightPt = linesNeeded * fontSizePt * 1.3;
            var heightPt = Math.Max(Math.Max(10.0, cluster.Height / pixelsPerPoint), estimatedHeightPt);

            // The VML fallback: fixed size, no "mso-fit-shape-to-text". A plain VML textbox without
            // auto-fit doesn't clip overflow by default, so a slightly-too-small estimate here
            // still shows all the text, just extending past the drawn box rather than losing
            // anything. (Its text stays horizontal in Word whatever the rotation says - that's
            // what the DrawingML shape below is for.)
            var style = string.Format(
                CultureInfo.InvariantCulture,
                "position:absolute;margin-left:{0:F2}pt;margin-top:{1:F2}pt;width:{2:F2}pt;height:{3:F2}pt;" +
                "rotation:{4:F1};mso-position-horizontal-relative:page;mso-position-vertical-relative:page;z-index:1",
                leftPt, topPt, widthPt, heightPt, cluster.AngleDegrees);

            var shapeId = shapeIdCounter;
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
            // A straight stamp's text comes as lines (see RotatedTextClusterer) - keep them as lines.
            var textLines = cluster.Text.Split('\n');
            for (var li = 0; li < textLines.Length; li++)
            {
                if (li > 0)
                    textRun.Append(new Break());
                textRun.Append(new Text(textLines[li]) { Space = SpaceProcessingModeValues.Preserve });
            }

            var textBox = new TextBox { Inset = "0,0,0,0" };
            textBox.Append(new TextBoxContent(new Paragraph((Run)textRun.CloneNode(true))));
            shape.Append(textBox);

            var drawing = BuildRotatedTextBox(shapeId, leftPt, topPt, widthPt, heightPt, cluster.AngleDegrees, textRun);
            paragraph.Append(new Run(new AlternateContent(
                new AlternateContentChoice(drawing) { Requires = "wps" },
                new AlternateContentFallback(new Picture(shape)))));
        }

        body.Append(paragraph);
    }

    /// <summary>
    /// A floating, page-positioned DrawingML text box rotated by <paramref name="angleDegrees"/>
    /// (clockwise, as in the source image) around its own center - the position is that of the
    /// unrotated box, as with VML. No fill, no outline, no auto-fit (see the size notes above).
    /// </summary>
    private static Drawing BuildRotatedTextBox(
        int id, double leftPt, double topPt, double widthPt, double heightPt, double angleDegrees, Run textRun)
    {
        const long emuPerPoint = 12700;
        var cx = (long)Math.Round(widthPt * emuPerPoint);
        var cy = (long)Math.Round(heightPt * emuPerPoint);
        var normalized = ((angleDegrees % 360) + 360) % 360;
        var rotation = (int)Math.Round(normalized * 60000) % 21600000;

        var shape = new Wps.WordprocessingShape(
            new Wps.NonVisualDrawingShapeProperties { TextBox = true },
            new Wps.ShapeProperties(
                new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }) { Rotation = rotation },
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill(),
                new A.Outline(new A.NoFill())),
            new Wps.TextBoxInfo2(new TextBoxContent(new Paragraph(textRun))),
            new Wps.TextBodyProperties(new A.NoAutoFit())
            {
                Rotation = 0,
                Vertical = A.TextVerticalValues.Horizontal,
                Wrap = A.TextWrappingValues.Square,
                LeftInset = 0,
                TopInset = 0,
                RightInset = 0,
                BottomInset = 0,
                Anchor = A.TextAnchoringTypeValues.Top,
            });

        var anchor = new Wp.Anchor(
            new Wp.SimplePosition { X = 0, Y = 0 },
            new Wp.HorizontalPosition(new Wp.PositionOffset(((long)Math.Round(leftPt * emuPerPoint)).ToString(CultureInfo.InvariantCulture)))
            {
                RelativeFrom = Wp.HorizontalRelativePositionValues.Page,
            },
            new Wp.VerticalPosition(new Wp.PositionOffset(((long)Math.Round(topPt * emuPerPoint)).ToString(CultureInfo.InvariantCulture)))
            {
                RelativeFrom = Wp.VerticalRelativePositionValues.Page,
            },
            new Wp.Extent { Cx = cx, Cy = cy },
            new Wp.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new Wp.WrapNone(),
            new Wp.DocProperties { Id = (UInt32Value)(uint)id, Name = $"ZayaRotated{id}" },
            new Wp.NonVisualGraphicFrameDrawingProperties(),
            new A.Graphic(new A.GraphicData(shape) { Uri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape" }))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
            SimplePos = false,
            RelativeHeight = (UInt32Value)(uint)(251658240 + id),
            BehindDoc = false,
            Locked = false,
            LayoutInCell = true,
            AllowOverlap = true,
        };

        return new Drawing(anchor);
    }

    /// <summary>
    /// Splits a Y-gap paragraph group into alignment-consistent runs (e.g. a centered heading
    /// followed by ordinary left-aligned body text) and appends one Word paragraph per run.
    /// </summary>
    private static void AppendAlignedParagraphs(
        Body body, IReadOnlyList<ScannedLine> groupLines, PageFontSizes fontSizes, double scale,
        float leftMargin, float rightMargin, float tolerance, double boldThreshold, DocxExportOptions options)
    {
        foreach (var (alignment, units) in DocumentLineGrouping.SplitByAlignment(groupLines, leftMargin, rightMargin, tolerance))
            body.Append(BuildParagraph(units, fontSizes, scale, alignment, boldThreshold, options));
    }

    private static Paragraph BuildParagraph(
        List<DocumentLineGrouping.SentenceUnit> units, PageFontSizes fontSizes, double scale, LineAlignment alignment,
        double boldThreshold, DocxExportOptions options)
    {
        // Sizes are per word (see FontSizeEstimator - usually one per line, occasionally a merged
        // fragment of another size); an exact line height has to fit the largest of them.
        double TargetPt(ScannedWord w) => Math.Max(options.FontSizeMinPt, fontSizes.ForWord(w) * scale);
        var allWords = units.SelectMany(u => u.Lines).SelectMany(l => l.Words).ToList();
        var targetFontSizePt = allWords.Count > 0
            ? allWords.Max(TargetPt)
            : Math.Max(options.FontSizeMinPt, units.SelectMany(u => u.Lines).Select(fontSizes.ForLine).DefaultIfEmpty(11f).Max() * scale);

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
        string? runHalfPoints = null;
        var pendingSpace = false;

        foreach (var (word, breakBefore) in words)
        {
            var isBold = options.BoldDetectionEnabled && word.InkDensity >= boldThreshold;
            var fontHalfPoints = ((int)Math.Round(TargetPt(word) * 2)).ToString();
            if (run is null || isBold != runIsBold || fontHalfPoints != runHalfPoints)
            {
                var runProperties = isBold
                    ? new RunProperties(new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily, ComplexScript = options.FontFamily }, new Bold(), new FontSize { Val = fontHalfPoints })
                    : new RunProperties(new RunFonts { Ascii = options.FontFamily, HighAnsi = options.FontFamily, ComplexScript = options.FontFamily }, new FontSize { Val = fontHalfPoints });
                run = new Run(runProperties);
                paragraph.Append(run);
                runIsBold = isBold;
                runHalfPoints = fontHalfPoints;
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

    private static Table BuildTable(
        TextTable table, PageFontSizes fontSizes, double scale,
        float rightMargin, float rightMarginTolerance, double boldThreshold, DocxExportOptions options, int marginTwips)
    {
        var (pageWidthTwips, _) = PageSizes.Get(options.PageSize);
        var usableWidthTwips = pageWidthTwips - 2 * marginTwips;

        // Column widths keep the scan's own proportions (see TableLayout.ColumnWidthFractions);
        // the last column takes the rounding remainder so the grid sums to the usable width.
        var widths = table.ColumnWidthFractions.Select(f => (int)Math.Round(f * usableWidthTwips)).ToList();
        widths[^1] = usableWidthTwips - widths.Take(widths.Count - 1).Sum();

        var result = new Table(
            new TableProperties(
                new TableWidth { Width = usableWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableBorders(
                    new TopBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new BottomBorder { Val = BorderValues.None },
                    new RightBorder { Val = BorderValues.None },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })),
            new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString() })));

        foreach (var row in table.Rows)
        {
            result.Append(new TableRow(row.Select((cellLines, c) =>
                BuildCell(cellLines, widths[c], fontSizes, scale, rightMargin, rightMarginTolerance, boldThreshold, options))));
        }

        return result;
    }

    private static TableCell BuildCell(
        IReadOnlyList<ScannedLine> lines, int widthTwips, PageFontSizes fontSizes, double scale, float rightMargin, float rightMarginTolerance,
        double boldThreshold, DocxExportOptions options)
    {
        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa }));

        // Every cell needs at least one paragraph; an empty one gets no spacing of its own so it
        // doesn't make its row taller than the cells that do have text.
        if (lines.Count == 0)
        {
            cell.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" })));
            return cell;
        }

        var units = DocumentLineGrouping.SplitIntoSentenceUnits(lines, rightMargin, rightMarginTolerance);
        cell.Append(BuildParagraph(units, fontSizes, scale, LineAlignment.Left, boldThreshold, options));
        return cell;
    }
}
