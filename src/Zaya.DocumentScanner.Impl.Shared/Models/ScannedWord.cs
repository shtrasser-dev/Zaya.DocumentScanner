namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// A single recognized word's pixel-space bounding box — kept alongside the line it belongs to
/// for style detection (e.g. bold via ink density) that needs finer granularity than a whole
/// line, and generally useful for debugging what the scan actually saw.
/// </summary>
/// <param name="InkDensity">
/// Fraction of this word's own bounding box darker than the page's ink/paper threshold (see
/// <c>InkDensityEstimator</c>) — a proxy for font weight, since none of this ecosystem's OCR
/// engines report bold/style directly.
/// </param>
/// <param name="TextHeight">
/// The word's own glyph height along its tilt: the mean of the OCR quadrilateral's two side edges
/// (P1-P4 and P2-P3), not the axis-aligned MaxY - MinY, which grows with rotation. Used by
/// <c>FontSizeEstimator</c> instead of the line's height, which is the layout engine's stabilized
/// display rail rather than a measurement.
/// </param>
public sealed record ScannedWord(
    string Text, float MinX, float MaxX, float MinY, float MaxY, double InkDensity, float TextHeight);
