namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// A group of OCR words whose own text angle marks them as rotated/curved content (e.g. text
/// around a round stamp) rather than part of the page's normal horizontal flow. Kept separate
/// from <see cref="ScannedLine"/> and rendered as its own rotated text block in the exported
/// document instead of being forced into the linear reading order at zero rotation, which a
/// genuinely rotated source can't survive without losing content or becoming unreadable.
/// </summary>
/// <param name="Text">Member words in reading order: for straight text set at one angle (an askew
/// rectangular/triangular stamp) its lines, separated by '\n'; for curved text (a round stamp) one
/// run of words ordered by angle around the cluster's center.</param>
/// <param name="CenterX">Horizontal center of the cluster's box, in source page pixels.</param>
/// <param name="CenterY">Vertical center of the cluster's box, in source page pixels.</param>
/// <param name="Width">Box width, in source page pixels. For straight text it is measured along
/// the text's own angle - the rotated box's real width. For curved text it is the page-aligned
/// bounding box: the cluster's spatial footprint (how far its words are spread around, e.g. a
/// stamp's diameter) - NOT how much space the text needs once laid out in an ordinary wrapped
/// text box; those can differ a lot for text that was originally arranged in a circle, so this is
/// a starting point for the export box size, not the final one.</param>
/// <param name="Height">Box height, in source page pixels - across the text's own angle for
/// straight text, page-aligned for curved text. Same caveat as <see cref="Width"/>.</param>
/// <param name="AngleDegrees">Representative rotation of the cluster's text, in degrees.</param>
/// <param name="TypicalWordHeightPx">Median height of the cluster's own member words, in source
/// page pixels - the right basis for the exported font size, unlike <see cref="Height"/> (which
/// reflects the cluster's spatial spread, not any single glyph's size).</param>
public sealed record RotatedTextCluster(
    string Text, float CenterX, float CenterY, float Width, float Height, float AngleDegrees,
    float TypicalWordHeightPx);
