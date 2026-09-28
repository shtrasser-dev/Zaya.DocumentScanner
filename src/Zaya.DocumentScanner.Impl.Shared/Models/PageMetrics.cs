namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>Pixel dimensions of the scanned page image, needed to reconstruct layout on export.</summary>
public sealed record PageMetrics(int WidthPx, int HeightPx);
