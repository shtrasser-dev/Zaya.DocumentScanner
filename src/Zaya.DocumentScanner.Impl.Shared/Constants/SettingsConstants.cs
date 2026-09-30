namespace Zaya.DocumentScanner.Impl.Shared.Constants;

public static class SettingsConstants
{
    /// <summary>Key under which DocumentScanner's own settings live inside a profile's settings dictionary.</summary>
    public const string DsKey = "documentScanner";

    /// <summary>Key under which the OCR engine's own settings live (OneOcrService).</summary>
    public const string OcrKey = "ocr";

    /// <summary>Key under which the text-layout engine's own settings live (ProximityTextLayoutService).</summary>
    public const string TextLayoutKey = "textLayout";

    public const string ProfileName = "profileName";
    public const string Step = "step";
    public const string MarginMultiplierPercent = "marginMultiplierPercent";
    public const string MissingFrameGrace = "missingFrameGrace";
    public const string WordGapThresholdPercent = "wordGapThresholdPercent";
    public const string LayoutTolerancePercent = "layoutTolerancePercent";
    public const string FontSizeMinPt = "fontSizeMinPt";
    public const string FontSizeMaxPt = "fontSizeMaxPt";
    public const string PageSize = "pageSize";
    public const string PageMarginMm = "pageMarginMm";
    public const string DocxFontFamily = "docxFontFamily";
    public const string BoldDetectionEnabled = "boldDetectionEnabled";

    public const string PageSizeA4 = "A4";
    public const string PageSizeLetter = "Letter";

    public static class EngineDefaults
    {
        public const string ProfileName = "По умолчанию";
    }
}
