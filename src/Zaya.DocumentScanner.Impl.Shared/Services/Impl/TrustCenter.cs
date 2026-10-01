using Zaya.Primitives.OCR;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>
/// Drops recognized lines whose entire bounding box lies within the margin band near the crop's
/// top or bottom edge - at this window position only a fragment of such a line could ever have
/// been captured, so it is not yet (or no longer) trustworthy. A line that merely overlaps a
/// margin band, with any part of it reaching the trusted core in between, is kept whole - every
/// one of its words, including the ones inside the margin - since there's no reason to distrust
/// words from a line that's otherwise confirmed.
///
/// This runs on lines, not individual OCR words: the OCR/layout step sees the full crop
/// (including the margin rows) so it has as much context as possible, and only the decision of
/// which resulting lines to trust happens here, after lines have been assembled.
///
/// The margin is relative to each line's own text height, not a fixed px count or a fraction of
/// window height - the risk zone is "about one line height from the edge," regardless of how
/// tall the window itself happens to be.
/// </summary>
public static class TrustCenter
{
    public static List<ITextLine> FilterEntirelyBuffered(
        IEnumerable<ITextLine> lines, int windowY, int windowHeight, float marginMultiplier)
    {
        var result = new List<ITextLine>();
        foreach (var line in lines)
        {
            var margin = line.Bounds.TextHeight > 0 ? line.Bounds.TextHeight * marginMultiplier : 20f;
            var windowLocalTop = line.Bounds.MinY - windowY;
            var windowLocalBottom = line.Bounds.MaxY - windowY;

            var entirelyInTopBuffer = windowLocalBottom <= margin;
            var entirelyInBottomBuffer = windowLocalTop >= windowHeight - margin;
            if (entirelyInTopBuffer || entirelyInBottomBuffer)
                continue;

            result.Add(line);
        }

        return result;
    }
}
