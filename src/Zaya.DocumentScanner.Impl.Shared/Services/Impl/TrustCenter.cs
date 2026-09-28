using Zaya.Primitives.OCR;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Drops OCR words close enough to the crop's top/bottom edge to plausibly be clipped mid-glyph.
/// The margin is relative to this frame's own median text height, not a fixed px count or a
/// fraction of window height — the risk zone is "about one line height from the edge,"
/// regardless of how tall the window itself happens to be.
/// </summary>
public static class TrustCenter
{
    public static List<IOCRWord> Filter(IOCRResult result, int windowHeight, float marginMultiplier)
    {
        var heights = result.Words.Select(w => w.Bounds.TextHeight).Where(h => h > 0).ToList();
        var lineHeight = heights.Count > 0 ? MathHelpers.Median(heights) : 20f;
        var margin = lineHeight * marginMultiplier;

        return result.Words
            .Where(w => w.Bounds.MinY >= margin && w.Bounds.MaxY <= windowHeight - margin)
            .ToList();
    }
}
