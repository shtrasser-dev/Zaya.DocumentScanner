using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Estimates page-level layout facts (text margins, font size) from the scanned lines' own
/// pixel positions, so the exported document approximates the source page rather than falling
/// back to arbitrary Word defaults.
/// </summary>
public static class PageLayoutEstimator
{
    /// <summary>A4 height in points (297 mm), used to convert the scanned page's pixel height to a real-world scale.</summary>
    private const float A4HeightPoints = 841.89f;

    /// <summary>
    /// Assumes the scanned image height corresponds to an A4 sheet (210x297mm) photographed to
    /// fill the frame — a real but declared approximation, not a detected page boundary. Measured
    /// against a real scan, treating OCR's glyph bounding-box height as directly proportional to
    /// the nominal font size (no extra correction factor) matched the source document's actual
    /// print size closely enough that adding one back only pushed sizes further off.
    /// </summary>
    public static float EstimateFontSizePoints(IReadOnlyList<ScannedLine> lines, int pageHeightPx)
    {
        if (lines.Count == 0 || pageHeightPx <= 0)
            return 11f;

        var pxToPt = A4HeightPoints / pageHeightPx;
        var fontSize = MedianHeight(lines) * pxToPt;
        // A wide upper bound: this is called per line/block, not once for the whole document, so
        // a genuine heading several times taller than body text must stay distinguishable instead
        // of being flattened to the same ceiling as every other large element.
        return Math.Clamp(fontSize, 8f, 28f);
    }

    private static float MedianHeight(IReadOnlyList<ScannedLine> lines)
    {
        var heights = lines.Select(l => l.Height).Where(h => h > 0).ToList();
        return heights.Count > 0 ? MathHelpers.Median(heights) : 20f;
    }
}
