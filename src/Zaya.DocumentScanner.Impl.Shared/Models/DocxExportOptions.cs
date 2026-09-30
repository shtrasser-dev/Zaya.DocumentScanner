namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>DOCX export parameters — see <see cref="DocumentScannerSettingDescriptors"/> for the matching UI settings.</summary>
public sealed class DocxExportOptions
{
    public string PageSize { get; init; } = "A4";

    /// <summary>
    /// The original page's own print margins (mm) — can't be detected from a photo, so it's a
    /// setting, like a print dialog's margins. Doubles as both the exported DOCX's page margin and
    /// the reference width (page width minus these margins) that the recognized text's own extent
    /// is scaled against to work out the real font size (see PageLayoutEstimator.EstimatePixelsPerPoint).
    /// </summary>
    public float PageMarginMm { get; init; } = 25.4f;

    public string FontFamily { get; init; } = "Calibri";

    public float FontSizeMinPt { get; init; } = 8f;

    public float FontSizeMaxPt { get; init; } = 28f;

    /// <summary>Percent of typical line height used as the tolerance for margins, centering and column detection.</summary>
    public float LayoutTolerancePercent { get; init; } = 250f;

    public bool BoldDetectionEnabled { get; init; }
}
