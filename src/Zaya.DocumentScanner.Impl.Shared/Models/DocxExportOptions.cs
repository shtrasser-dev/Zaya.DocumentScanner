namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>DOCX export parameters — see <see cref="DocumentScannerSettingDescriptors"/> for the matching UI settings.</summary>
public sealed class DocxExportOptions
{
    public string PageSize { get; init; } = "A4";

    public string FontFamily { get; init; } = "Calibri";

    public float FontSizeMinPt { get; init; } = 8f;

    public float FontSizeMaxPt { get; init; } = 28f;

    /// <summary>Percent of typical line height used as the tolerance for margins, centering and column detection.</summary>
    public float LayoutTolerancePercent { get; init; } = 250f;

    public bool BoldDetectionEnabled { get; init; }
}
