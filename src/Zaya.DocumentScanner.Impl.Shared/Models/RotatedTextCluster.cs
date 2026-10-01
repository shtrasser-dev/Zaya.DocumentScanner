namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// A group of OCR words whose own text angle marks them as rotated/curved content (e.g. text
/// around a round stamp) rather than part of the page's normal horizontal flow. Kept separate
/// from <see cref="ScannedLine"/> and rendered as its own rotated text block in the exported
/// document instead of being forced into the linear reading order at zero rotation, which a
/// genuinely rotated source can't survive without losing content or becoming unreadable.
/// </summary>
/// <param name="Text">Member words joined in roughly the order they'd be read around the cluster.</param>
/// <param name="CenterX">Horizontal center of the cluster's bounding box, in source page pixels.</param>
/// <param name="CenterY">Vertical center of the cluster's bounding box, in source page pixels.</param>
/// <param name="Width">Bounding box width, in source page pixels.</param>
/// <param name="Height">Bounding box height, in source page pixels.</param>
/// <param name="AngleDegrees">Representative rotation of the cluster's text, in degrees.</param>
public sealed record RotatedTextCluster(
    string Text, float CenterX, float CenterY, float Width, float Height, float AngleDegrees);
