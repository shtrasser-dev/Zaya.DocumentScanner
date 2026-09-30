using Zaya.DocumentScanner.Impl.Shared.Constants;

namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// Page dimensions in twips (1/20 pt), shared by the DOCX exporter and the on-screen "Результат"
/// preview so both agree on the same physical page size.
/// </summary>
public static class PageSizes
{
    public const int A4WidthTwips = 11906;
    public const int A4HeightTwips = 16838;
    public const int LetterWidthTwips = 12240;
    public const int LetterHeightTwips = 15840;

    /// <summary>1 inch, kept only as the settings descriptor's default - the effective margin at runtime always comes from <see cref="MarginTwipsFromMm"/>.</summary>
    public const int MarginTwips = 1440;

    public static (int WidthTwips, int HeightTwips) Get(string pageSize) =>
        pageSize == SettingsConstants.PageSizeLetter ? (LetterWidthTwips, LetterHeightTwips) : (A4WidthTwips, A4HeightTwips);

    public static int MarginTwipsFromMm(float marginMm) => (int)Math.Round(marginMm / 25.4 * 1440);
}
