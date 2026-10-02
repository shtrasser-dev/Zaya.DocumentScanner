using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Works out a global shrink factor for one page's font sizes, as a safety net on top of
/// <see cref="PageLayoutEstimator.EstimatePixelsPerPoint"/>. That estimate is calibrated from the
/// page's overall text width, not from how any individual paragraph will actually wrap once
/// re-rendered at a possibly different page width/margins - real documents can therefore still
/// come out a few percent taller than one physical page. Rather than leave that to be found only
/// after opening the exported .docx (or worse, hand-tuned per document via the margin/font-size
/// settings), this estimates the same total height DocxExportService/DocumentPreviewBuilder will
/// produce and, if it doesn't fit, returns a factor &lt; 1 to shrink every paragraph's font size by
/// (preserving heading-vs-body proportions, since it multiplies rather than replaces the estimate).
/// Never rescues a page that's genuinely too long: once every paragraph is already down at
/// <see cref="DocxExportOptions.FontSizeMinPt"/>, applying the factor further would cross that
/// floor, so callers apply it as `Math.Max(minPt, baseFontSizePt * scale)` - a page with truly too
/// much content is correctly left to spill onto more than one page.
/// </summary>
public static class PageFitScaler
{
    // Word's "single" line spacing (SpacingBetweenLines Line="240" LineRule="Auto", used throughout
    // this exporter) renders each line taller than its bare nominal font size - this is a fixed
    // approximation of that overhead, not a per-font measurement. DocxExportService reuses this
    // same constant when a correction is applied via an exact line height (see BuildParagraph), so
    // what actually gets rendered matches what this estimate assumed.
    public const double LineSpacingFactor = 1.2;

    // A margin of safety on top of the bare "just barely fits" target: word-wrap, table rows and
    // the line-count proxy below are all approximations of what Word will really do, so aiming for
    // exactly 100% of the usable height would still leave some real documents a hair over the edge.
    private const double SafetyMargin = 0.9;

    public static double EstimateScale(
        IReadOnlyList<ScannedLine> lines, PageMetrics page, PageFontSizes fontSizes, DocxExportOptions options)
    {
        var paragraphs = DocumentLineGrouping.GroupIntoParagraphs(lines);
        if (paragraphs.Count == 0)
            return 1.0;

        var leftMargin = PageLayoutEstimator.EstimateLeftMargin(lines);
        var rightMargin = PageLayoutEstimator.EstimateRightMargin(lines);
        var tolerance = PageLayoutEstimator.EstimateTypicalHeight(lines) * (options.LayoutTolerancePercent / 100f);

        var totalPt = 0.0;
        foreach (var block in paragraphs.SelectMany(DocumentLineGrouping.SplitIntoBlocks))
        {
            if (!block.IsColumns)
            {
                totalPt += EstimateGroupHeightPt(block.Lines, fontSizes, leftMargin, rightMargin, tolerance, options);
                continue;
            }

            // The two columns render side by side, in one table row - the row's height is the
            // taller of the two, not their sum.
            var leftUnits = DocumentLineGrouping.SplitIntoSentenceUnits(block.Left!, rightMargin, tolerance);
            var rightUnits = DocumentLineGrouping.SplitIntoSentenceUnits(block.Right!, rightMargin, tolerance);
            totalPt += Math.Max(
                EstimateUnitsHeightPt(leftUnits, fontSizes),
                EstimateUnitsHeightPt(rightUnits, fontSizes));
        }

        var (_, pageHeightTwips) = PageSizes.Get(options.PageSize);
        var pageHeightPt = pageHeightTwips / 20.0;
        var marginPt = options.PageMarginMm * (72.0 / 25.4);
        var usableHeightPt = Math.Max(1.0, pageHeightPt - 2 * marginPt) * SafetyMargin;

        return totalPt <= usableHeightPt || totalPt <= 0 ? 1.0 : usableHeightPt / totalPt;
    }

    private static double EstimateGroupHeightPt(
        IReadOnlyList<ScannedLine> groupLines, PageFontSizes fontSizes,
        float leftMargin, float rightMargin, float tolerance, DocxExportOptions options)
    {
        var sum = 0.0;
        foreach (var (_, units) in DocumentLineGrouping.SplitByAlignment(groupLines, leftMargin, rightMargin, tolerance))
            sum += EstimateUnitsHeightPt(units, fontSizes);
        return sum;
    }

    private static double EstimateUnitsHeightPt(
        List<DocumentLineGrouping.SentenceUnit> units, PageFontSizes fontSizes)
    {
        // Each sentence unit becomes one hard-broken line in the output (see BuildParagraph) - the
        // lines merged into it by word-wrap are folded back to however many lines it had originally,
        // a reasonable proxy since the width-based scale is calibrated to reproduce the source's own
        // line-wrap proportions.
        return units.SelectMany(u => u.Lines).Sum(l => fontSizes.ForLine(l)) * LineSpacingFactor;
    }
}
