using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Estimates page-level layout facts (text margins, font size) from the scanned lines' own
/// pixel positions, so the exported document approximates the source page rather than falling
/// back to arbitrary Word defaults.
/// </summary>
public static class PageLayoutEstimator
{
    private const double PointsPerMm = 72.0 / 25.4;

    /// <summary>
    /// Pixels-per-point for this page, derived from the printed text's own left/right extent rather
    /// than the raw frame height. A height-based scale silently assumes the photo shows the whole
    /// page top-to-bottom - true for a wide establishing shot, but wrong for a tight close-up crop
    /// that only captures PART of the page's height (that photo's frame maps to less real-world mm
    /// than a full sheet, so the old height-based estimate reads its font size as larger than it
    /// really is, and text sized for one page overflows onto more).
    /// <para/>
    /// The text's horizontal extent doesn't have that problem: as long as the crop captures the
    /// full printed width (nearly always true, since documents are usually held/shot upright and
    /// width-cropping a page mid-line is rare), it reflects the real printable-area width regardless
    /// of vertical cropping. That width is anchored to <paramref name="marginMm"/> - the printed
    /// page's own margins, which can't be detected from the photo and are taken as a user setting,
    /// the same way a print dialog asks for them - giving printableWidth = pageWidth - 2*margin as
    /// the known real-world distance the detected text span corresponds to.
    /// </summary>
    public static double EstimatePixelsPerPoint(IReadOnlyList<ScannedLine> lines, string pageSize, float marginMm)
    {
        if (lines.Count == 0)
            return 1.0;

        var textWidthPx = Math.Max(1f, EstimateRightMargin(lines) - EstimateLeftMargin(lines));

        var (pageWidthTwips, _) = PageSizes.Get(pageSize);
        var pageWidthPt = pageWidthTwips / 20.0;
        var marginPt = marginMm * PointsPerMm;
        var printableWidthPt = Math.Max(1.0, pageWidthPt - 2 * marginPt);

        return textWidthPx / printableWidthPt;
    }

    /// <summary>
    /// Converts a measured line height to points using <paramref name="pixelsPerPoint"/> (see
    /// <see cref="EstimatePixelsPerPoint"/>). Treats OCR's glyph bounding-box height as directly
    /// proportional to the nominal font size (no extra correction factor) - matched a real scan's
    /// actual print size closely enough that adding one back only pushed sizes further off.
    /// </summary>
    public static float EstimateFontSizePoints(
        IReadOnlyList<ScannedLine> lines, double pixelsPerPoint, float minPt = 8f, float maxPt = 28f)
    {
        if (lines.Count == 0 || pixelsPerPoint <= 0)
            return 11f;

        var fontSize = MedianHeight(lines) / pixelsPerPoint;
        // Only the fallback for a page with no measurable words now - FontSizeEstimator decides
        // sizes from word heights (a line's height is the layout engine's display rail, not a
        // measurement). The upper bound stays wide so a genuinely large element isn't flattened.
        return Math.Clamp((float)fontSize, minPt, maxPt);
    }

    /// <summary>
    /// A high percentile (not the max) of every line's right edge, robust to the occasional line
    /// that overruns into the margin — used to tell a genuinely word-wrapped line (one that runs
    /// all the way out to this edge) from a short line that just happens not to end in a period.
    /// </summary>
    public static float EstimateRightMargin(IReadOnlyList<ScannedLine> lines)
    {
        if (lines.Count == 0)
            return 0f;

        var sorted = lines.Select(l => l.MaxX).OrderBy(v => v).ToList();
        var index = (int)Math.Clamp(0.9f * (sorted.Count - 1), 0, sorted.Count - 1);
        return sorted[index];
    }

    /// <summary>Mirror of <see cref="EstimateRightMargin"/> for the left edge (low percentile).</summary>
    public static float EstimateLeftMargin(IReadOnlyList<ScannedLine> lines)
    {
        if (lines.Count == 0)
            return 0f;

        var sorted = lines.Select(l => l.MinX).OrderBy(v => v).ToList();
        var index = (int)Math.Clamp(0.1f * (sorted.Count - 1), 0, sorted.Count - 1);
        return sorted[index];
    }

    /// <summary>Typical (median) line height in px, for scaling other layout tolerances.</summary>
    public static float EstimateTypicalHeight(IReadOnlyList<ScannedLine> lines) => MedianHeight(lines);

    /// <summary>
    /// The ink-density value that best separates this document's own words into two weight
    /// classes (Otsu split, not a fixed constant — validated against a real scan, whole-word ink
    /// density clusters cleanly into "bold" and "regular" groups, but the exact values are
    /// page/lighting-dependent, so the cutoff has to be found per document).
    /// </summary>
    public static double EstimateBoldDensityThreshold(IReadOnlyList<ScannedLine> lines)
        => InkDensityEstimator.OtsuSplit(lines.SelectMany(l => l.Words).Select(w => w.InkDensity).ToList());

    private static float MedianHeight(IReadOnlyList<ScannedLine> lines)
    {
        var heights = lines.Select(l => l.Height).Where(h => h > 0).ToList();
        return heights.Count > 0 ? MathHelpers.Median(heights) : 20f;
    }
}
