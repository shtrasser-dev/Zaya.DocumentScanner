using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Renders the same paragraph structure <see cref="DocxExportService"/> writes to DOCX (grouping,
/// alignment, two-column letterhead blocks, per-word bold) as an Avalonia visual tree, so the
/// "Результат" tab shows a page-like approximation of the exported document without depending on
/// an external Word renderer.
/// </summary>
public static class DocumentPreviewBuilder
{
    private const double DisplayWidthPx = 650;
    private const double PageGap = 24;

    /// <summary>Stacks one rendered "page" per source image, in order - how several scans combine into one document.</summary>
    public static Control Build(IReadOnlyList<ScannedPage> pages, DocxExportOptions options)
    {
        var stack = new StackPanel { Spacing = PageGap };
        foreach (var page in pages)
            stack.Children.Add(BuildPage(page.Lines, page.Metrics, options));
        return stack;
    }

    private static Control BuildPage(IReadOnlyList<ScannedLine> lines, PageMetrics page, DocxExportOptions options)
    {
        var (pageWidthTwips, pageHeightTwips) = PageSizes.Get(options.PageSize);
        var pageWidthPt = pageWidthTwips / 20.0;
        var pageHeightPt = pageHeightTwips / 20.0;
        var scale = DisplayWidthPx / pageWidthPt;
        var marginPx = options.PageMarginMm * (72.0 / 25.4) * scale;

        var body = new StackPanel { Spacing = 0 };
        var paragraphs = DocumentLineGrouping.GroupIntoParagraphs(lines);
        if (paragraphs.Count > 0)
        {
            var pixelsPerPoint = PageLayoutEstimator.EstimatePixelsPerPoint(lines, options.PageSize, options.PageMarginMm);
            var fontSizes = FontSizeEstimator.Estimate(lines, pixelsPerPoint, options.FontSizeMinPt, options.FontSizeMaxPt);
            var fitScale = PageFitScaler.EstimateScale(lines, page, fontSizes, options);
            var leftMargin = PageLayoutEstimator.EstimateLeftMargin(lines);
            var rightMargin = PageLayoutEstimator.EstimateRightMargin(lines);
            var tolerance = PageLayoutEstimator.EstimateTypicalHeight(lines) * (options.LayoutTolerancePercent / 100f);
            var boldThreshold = PageLayoutEstimator.EstimateBoldDensityThreshold(lines);

            foreach (var block in paragraphs.SelectMany(DocumentLineGrouping.SplitIntoBlocks))
            {
                if (block.IsColumns)
                    body.Children.Add(BuildColumnsGrid(block.Left!.ToList(), block.Right!.ToList(), page, fontSizes, fitScale, rightMargin, tolerance, boldThreshold, options, scale));
                else
                    AppendAligned(body, block.Lines, fontSizes, fitScale, leftMargin, rightMargin, tolerance, boldThreshold, options, scale);
            }
        }

        // A fixed (not minimum) height, clipped: an infinitely-growing preview would always look
        // like "one page" no matter how much text there is, hiding exactly the mismatch a real
        // Word page count has to deal with. Measuring here and flagging overflow tells the user
        // up front that this scan won't fit on a single exported page, instead of only finding out
        // after opening the .docx.
        var pageHeightPx = Math.Max(1, pageHeightPt * scale);
        var availableWidth = Math.Max(1, DisplayWidthPx - 2 * marginPx);
        var availableHeight = Math.Max(1, pageHeightPx - 2 * marginPx);
        body.Measure(new Size(availableWidth, double.PositiveInfinity));
        var overflows = body.DesiredSize.Height > availableHeight;

        var pageBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(overflows ? Color.FromRgb(210, 70, 70) : Color.FromRgb(200, 200, 200)),
            BorderThickness = new Avalonia.Thickness(overflows ? 2 : 1),
            Width = DisplayWidthPx,
            Height = pageHeightPx,
            ClipToBounds = true,
            Padding = new Avalonia.Thickness(marginPx),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = body,
        };

        if (!overflows)
            return pageBorder;

        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "Не помещается на одну страницу — в DOCX эта страница займёт несколько листов",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                pageBorder,
            },
        };
    }

    private static void AppendAligned(
        StackPanel body, IReadOnlyList<ScannedLine> groupLines, PageFontSizes fontSizes, double fitScale,
        float leftMargin, float rightMargin, float tolerance, double boldThreshold, DocxExportOptions options, double scale)
    {
        foreach (var (alignment, units) in DocumentLineGrouping.SplitByAlignment(groupLines, leftMargin, rightMargin, tolerance))
            body.Children.Add(BuildParagraphBlock(units, fontSizes, fitScale, alignment, boldThreshold, options, scale));
    }

    private static Grid BuildColumnsGrid(
        List<ScannedLine> left, List<ScannedLine> right, PageMetrics page, PageFontSizes fontSizes, double fitScale,
        float rightMargin, float rightMarginTolerance, double boldThreshold, DocxExportOptions options, double scale)
    {
        var splitX = (left.Max(l => l.MaxX) + right.Min(l => l.MinX)) / 2.0;
        var leftFraction = Math.Clamp(page.WidthPx <= 0 ? 0.5 : splitX / page.WidthPx, 0.15, 0.85);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(leftFraction, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1 - leftFraction, GridUnitType.Star));

        var leftUnits = DocumentLineGrouping.SplitIntoSentenceUnits(left, rightMargin, rightMarginTolerance);
        var rightUnits = DocumentLineGrouping.SplitIntoSentenceUnits(right, rightMargin, rightMarginTolerance);

        var leftBlock = BuildParagraphBlock(leftUnits, fontSizes, fitScale, LineAlignment.Left, boldThreshold, options, scale);
        var rightBlock = BuildParagraphBlock(rightUnits, fontSizes, fitScale, LineAlignment.Left, boldThreshold, options, scale);
        Grid.SetColumn(rightBlock, 1);

        grid.Children.Add(leftBlock);
        grid.Children.Add(rightBlock);
        return grid;
    }

    private static TextBlock BuildParagraphBlock(
        List<DocumentLineGrouping.SentenceUnit> units, PageFontSizes fontSizes, double fitScale, LineAlignment alignment,
        double boldThreshold, DocxExportOptions options, double scale)
    {
        // Per-word sizes, same as DocxExportService.BuildParagraph; the block's own size (and exact
        // line height) is the largest of them.
        double TargetPt(ScannedWord w) => Math.Max(options.FontSizeMinPt, fontSizes.ForWord(w) * fitScale);
        var allWords = units.SelectMany(u => u.Lines).SelectMany(l => l.Words).ToList();
        var fontSizePt = allWords.Count > 0
            ? allWords.Max(TargetPt)
            : Math.Max(options.FontSizeMinPt, units.SelectMany(u => u.Lines).Select(fontSizes.ForLine).DefaultIfEmpty(11f).Max() * fitScale);

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(options.FontFamily),
            FontSize = Math.Max(4, fontSizePt * scale),
            TextAlignment = alignment switch
            {
                LineAlignment.Center => TextAlignment.Center,
                LineAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };

        // Mirrors DocxExportService.BuildParagraph's exact line-height correction, so the preview's
        // own overflow check (below, in BuildPage) agrees with what the real export will produce
        // instead of still showing an overflow warning a PageFitScaler correction already resolved.
        if (fitScale < 0.999)
            textBlock.LineHeight = fontSizePt * PageFitScaler.LineSpacingFactor * scale;

        // Same flattening as DocxExportService.BuildParagraph (shared via DocumentLineGrouping):
        // one entry per original word, a hard break at the start of a new sentence unit or before
        // a word that follows an abnormally wide gap within its own source line, no smoothing
        // (see that method's remarks).
        var words = DocumentLineGrouping.FlattenWords(units);

        var pendingSpace = false;
        foreach (var (word, breakBefore) in words)
        {
            if (breakBefore)
                textBlock.Inlines!.Add(new LineBreak());

            var isBold = options.BoldDetectionEnabled && word.InkDensity >= boldThreshold;
            textBlock.Inlines!.Add(new Run((pendingSpace ? " " : "") + word.Text)
            {
                FontWeight = isBold ? FontWeight.Bold : FontWeight.Normal,
                FontSize = Math.Max(4, TargetPt(word) * scale),
            });
            pendingSpace = true;
        }

        return textBlock;
    }
}
