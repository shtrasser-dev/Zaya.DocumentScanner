using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.Primitives;
using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// DocumentScanner's own settings — the sliding-window scan and DOCX export heuristics tuned
/// against a real scanned document, now declared the same way an OCR/TextLayout plugin declares
/// its own settings, instead of being hardcoded constants scattered across the services that use them.
/// </summary>
public static class DocumentScannerSettingDescriptors
{
    public const string DsKey = SettingsConstants.DsKey;

    public const string ProfileName = SettingsConstants.ProfileName;
    public const string Step = SettingsConstants.Step;
    public const string MarginMultiplierPercent = SettingsConstants.MarginMultiplierPercent;
    public const string MissingFrameGrace = SettingsConstants.MissingFrameGrace;
    public const string WordGapThresholdPercent = SettingsConstants.WordGapThresholdPercent;
    public const string LayoutTolerancePercent = SettingsConstants.LayoutTolerancePercent;
    public const string FontSizeMinPt = SettingsConstants.FontSizeMinPt;
    public const string FontSizeMaxPt = SettingsConstants.FontSizeMaxPt;
    public const string PageSize = SettingsConstants.PageSize;
    public const string PageMarginMm = SettingsConstants.PageMarginMm;
    public const string DocxFontFamily = SettingsConstants.DocxFontFamily;
    public const string BoldDetectionEnabled = SettingsConstants.BoldDetectionEnabled;

    public static readonly IReadOnlyList<SettingDescriptor> All =
    [
        new StringSettingDescriptor(ProfileName, LocalizedString.Invariant("Имя профиля"))
        {
            Description = LocalizedString.Invariant("Название этого набора настроек — под ним он сохраняется на диск."),
            DefaultValue = SettingsConstants.EngineDefaults.ProfileName,
        },

        new IntegerSettingDescriptor(Step, LocalizedString.Invariant("Шаг окна распознавания (px)"))
        {
            Description = LocalizedString.Invariant(
                "Насколько сдвигается окно распознавания вниз между кадрами. Меньше — точнее, но дольше."),
            DefaultValue = 15,
            MinValue = 1,
            MaxValue = 200,
        },
        new IntegerSettingDescriptor(MarginMultiplierPercent, LocalizedString.Invariant("Отступ от края окна (%)"))
        {
            Description = LocalizedString.Invariant(
                "Слова ближе этого множителя (в % от высоты строки) к верхнему/нижнему краю окна отбрасываются — " +
                "они рискуют быть обрезаны по букве."),
            DefaultValue = 120,
            MinValue = 0,
            MaxValue = 500,
        },
        new IntegerSettingDescriptor(MissingFrameGrace, LocalizedString.Invariant("Кадров ожидания перед потерей строки"))
        {
            Description = LocalizedString.Invariant(
                "Сколько кадров подряд строка может отсутствовать, прежде чем считается ушедшей из окна."),
            DefaultValue = 3,
            MinValue = 0,
            MaxValue = 50,
        },
        new IntegerSettingDescriptor(WordGapThresholdPercent, LocalizedString.Invariant("Порог склейки слов в строку (%)"))
        {
            Description = LocalizedString.Invariant(
                "Максимальный горизонтальный зазор (в % от высоты строки), при котором соседние слова " +
                "ещё считаются одной строкой."),
            DefaultValue = 150,
            MinValue = 10,
            MaxValue = 1000,
        },
        new IntegerSettingDescriptor(LayoutTolerancePercent, LocalizedString.Invariant("Допуск разметки страницы (%)"))
        {
            Description = LocalizedString.Invariant(
                "Допуск (в % от типичной высоты строки) для определения полей страницы, центрирования и " +
                "разбиения на колонки."),
            DefaultValue = 250,
            MinValue = 10,
            MaxValue = 2000,
        },
        new IntegerSettingDescriptor(FontSizeMinPt, LocalizedString.Invariant("Минимальный размер шрифта (пт)"))
        {
            Description = LocalizedString.Invariant("Нижняя граница автоподбора размера шрифта в экспортированном документе."),
            DefaultValue = 8,
            MinValue = 4,
            MaxValue = 72,
        },
        new IntegerSettingDescriptor(FontSizeMaxPt, LocalizedString.Invariant("Максимальный размер шрифта (пт)"))
        {
            Description = LocalizedString.Invariant("Верхняя граница автоподбора размера шрифта в экспортированном документе."),
            DefaultValue = 28,
            MinValue = 4,
            MaxValue = 144,
        },
        new EnumSettingDescriptor(PageSize, LocalizedString.Invariant("Размер листа"))
        {
            Description = LocalizedString.Invariant("Размер страницы в экспортированном Word-документе."),
            DefaultValue = SettingsConstants.PageSizeA4,
            Options =
            [
                new EnumOption(SettingsConstants.PageSizeA4, LocalizedString.Invariant("A4")),
                new EnumOption(SettingsConstants.PageSizeLetter, LocalizedString.Invariant("Letter")),
            ],
        },
        new IntegerSettingDescriptor(PageMarginMm, LocalizedString.Invariant("Поля листа (мм)"))
        {
            Description = LocalizedString.Invariant(
                "Поля печатной страницы оригинала — определить их по фото нельзя, поэтому задаются вручную, как при печати. " +
                "Используются, чтобы по ширине распознанного текста вычислить реальный размер шрифта: если в кадр попал не " +
                "весь лист по высоте (а не только вплотную к тексту), расчёт от высоты кадра завышает шрифт и текст не " +
                "помещается на страницу при экспорте — расчёт от ширины печатной области устойчив к этому."),
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
        },
        new StringSettingDescriptor(DocxFontFamily, LocalizedString.Invariant("Шрифт Word"))
        {
            Description = LocalizedString.Invariant("Семейство шрифта, которым набирается текст в экспортированном документе."),
            DefaultValue = "Calibri",
        },
        new BooleanSettingDescriptor(BoldDetectionEnabled, LocalizedString.Invariant("Определять жирный текст"))
        {
            Description = LocalizedString.Invariant(
                "Экспериментально: пытаться распознать жирные слова по плотности чернил. Пока даёт много " +
                "ложных срабатываний на числах и коротких словах — выключено по умолчанию."),
            DefaultValue = false,
        },
    ];
}
