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
    public const string StepPercentOfLine = SettingsConstants.StepPercentOfLine;
    public const string MarginMultiplierPercent = SettingsConstants.MarginMultiplierPercent;
    public const string MissingFrameGrace = SettingsConstants.MissingFrameGrace;
    public const string WordGapThresholdPercent = SettingsConstants.WordGapThresholdPercent;
    public const string MaxWordAngleDeviationDegrees = SettingsConstants.MaxWordAngleDeviationDegrees;
    public const string RespectOcrLines = SettingsConstants.RespectOcrLines;
    public const string WindowLines = SettingsConstants.WindowLines;
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

        new IntegerSettingDescriptor(WindowLines, LocalizedString.Invariant("Высота окна распознавания (строк)"))
        {
            Description = LocalizedString.Invariant(
                "Сколько строк текста помещается в окно распознавания. Высота строки определяется по " +
                "быстрому предварительному проходу по всей странице, поэтому окно подстраивается под " +
                "размер шрифта. Слишком мало строк даёт дубли, слишком много — медленнее и хуже на мелком тексте."),
            DefaultValue = 8,
            MinValue = 3,
            MaxValue = 40,
        },
        new IntegerSettingDescriptor(StepPercentOfLine, LocalizedString.Invariant("Шаг окна распознавания (% от высоты строки)"))
        {
            Description = LocalizedString.Invariant(
                "Насколько сдвигается окно распознавания вниз между кадрами, в процентах от расстояния между " +
                "строками этой страницы. 200 — на две строки: на тестах так же точно, как мелкий шаг, но в 3–4 раза " +
                "быстрее. Слишком мелкий шаг (меньше строки) не точнее, а иногда даёт дубли."),
            DefaultValue = 200,
            MinValue = 10,
            MaxValue = 400,
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
        new IntegerSettingDescriptor(MaxWordAngleDeviationDegrees, LocalizedString.Invariant("Макс. отклонение угла слова (°)"))
        {
            Description = LocalizedString.Invariant(
                "Слова, чей угол наклона отличается от типичного угла этого кадра больше чем на столько градусов, " +
                "выводятся отдельными повёрнутыми текстовыми блоками вместо обычных строк — так печати и штампы " +
                "(текст которых идёт по дуге под разными углами) не портят обычный текст рядом с собой. " +
                "180 — отключает выделение, такие слова просто остаются обычным текстом."),
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 180,
        },
        new BooleanSettingDescriptor(RespectOcrLines, LocalizedString.Invariant("Учитывать строки OCR"))
        {
            Description = LocalizedString.Invariant(
                "Не склеивать в одну строку слова, которые движок OCR сам отнёс к разным строкам (например, " +
                "соседние ячейки таблицы с узким промежутком между ними). Широкий порог склейки слов выше нужен " +
                "для пар «метка — значение», но без этой проверки он перекидывает слова через границу ячейки."),
            DefaultValue = true,
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
