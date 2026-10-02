using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>Maps the active profile's descriptor values to the plain option objects the scan/export services take.</summary>
public static class ScanOptionsFactory
{
    public static DocumentScanOptions BuildScanOptions(SettingDescriptorList? ds)
    {
        if (ds is null)
            return new DocumentScanOptions();

        return new DocumentScanOptions
        {
            StepPercentOfLine = ds.GetValueAsInt(SettingsConstants.StepPercentOfLine),
            MarginMultiplier = ds.GetValueAsInt(SettingsConstants.MarginMultiplierPercent) / 100f,
            MissingFrameGrace = ds.GetValueAsInt(SettingsConstants.MissingFrameGrace),
            WordGapThresholdPercent = ds.GetValueAsInt(SettingsConstants.WordGapThresholdPercent),
            MaxWordAngleDeviationDegrees = ds.GetValueAsInt(SettingsConstants.MaxWordAngleDeviationDegrees),
            RespectOcrLines = ds.GetValueAsBool(SettingsConstants.RespectOcrLines),
            WindowLines = ds.GetValueAsInt(SettingsConstants.WindowLines),
        };
    }

    public static DocxExportOptions BuildExportOptions(SettingDescriptorList? ds)
    {
        if (ds is null)
            return new DocxExportOptions();

        return new DocxExportOptions
        {
            PageSize = ds.GetValueAsString(SettingsConstants.PageSize),
            PageMarginMm = ds.GetValueAsInt(SettingsConstants.PageMarginMm),
            FontFamily = ds.GetValueAsString(SettingsConstants.DocxFontFamily),
            FontSizeMinPt = ds.GetValueAsInt(SettingsConstants.FontSizeMinPt),
            FontSizeMaxPt = ds.GetValueAsInt(SettingsConstants.FontSizeMaxPt),
            LayoutTolerancePercent = ds.GetValueAsInt(SettingsConstants.LayoutTolerancePercent),
            BoldDetectionEnabled = ds.GetValueAsBool(SettingsConstants.BoldDetectionEnabled),
        };
    }
}
