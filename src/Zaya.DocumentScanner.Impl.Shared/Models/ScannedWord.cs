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
public sealed record ScannedWord(string Text, float MinX, float MaxX, float MinY, float MaxY, double InkDensity);
