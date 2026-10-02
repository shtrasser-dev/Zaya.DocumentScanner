namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// Sliding-window scan parameters. Defaults are the values validated against a real
/// photographed (tilted, non-flat-scan) document: dense per-pixel stepping is both far
/// slower and worse quality than a modest step combined with trust-center filtering.
/// </summary>
public sealed class DocumentScanOptions
{
    /// <summary>Recognition window height in px. Null derives it from <see cref="WindowLines"/> (see DocumentScanService).</summary>
    public int? WindowHeight { get; init; }

    /// <summary>
    /// Window height in text lines of this page (its own line pitch, measured by a quick
    /// full-page OCR pass), used when <see cref="WindowHeight"/> isn't set. ~6-10 lines did best
    /// across pages with very different text sizes; too few gave duplicate readings, too many
    /// was slower and less accurate on small text.
    /// </summary>
    public int WindowLines { get; init; } = 8;

    /// <summary>
    /// How far the window moves down per frame, in percent of this page's line pitch (200 = two
    /// lines). On a test set 200% matched the old fixed 15px step in quality at 3-4x the speed,
    /// quality held up to ~300%, and steps under one line were no better - sometimes worse, as
    /// each line passed through so many frames that its readings fragmented into duplicates.
    /// </summary>
    public int StepPercentOfLine { get; init; } = 200;

    /// <summary>
    /// Words within this many multiples of the frame's own median text height from the
    /// crop's top/bottom edge are dropped before layout — they're at risk of being clipped
    /// mid-glyph by the window boundary.
    /// </summary>
    public float MarginMultiplier { get; init; } = 1.2f;

    /// <summary>Consecutive frames a line may be absent before it's treated as scrolled out of view.</summary>
    public int MissingFrameGrace { get; init; } = 3;

    /// <summary>
    /// Max horizontal gap (percent of line height) the layout engine bridges when merging words
    /// into one line. The default (50) is tuned for on-screen text; scanned documents can have
    /// wider clause-to-clause gaps within a single printed line (e.g. label/value lists), which
    /// at the default split into separate, misordered fragments instead of one line.
    /// </summary>
    public int WordGapThresholdPercent { get; init; } = 150;

    /// <summary>
    /// Words whose own text angle deviates from this frame's typical (median) word angle by more
    /// than this many degrees are set aside as rotated content instead of being fed to line
    /// assembly (see RotatedTextClusterer). Ordinary printed text sits at one consistent angle
    /// (give or take a photographed page's own small, uniform tilt); a round stamp or seal's text
    /// follows the curve, so its words swing through a wide range of angles word to word. 180
    /// effectively disables this (no real angle delta exceeds it).
    /// </summary>
    public float MaxWordAngleDeviationDegrees { get; init; } = 30f;

    /// <summary>
    /// Never let the layout engine join words the OCR engine itself put in different lines of the
    /// same frame. WordGapThresholdPercent is widened for label/value lines, and that width also
    /// bridges the narrow gap between two neighboring table cells - while OneOCR's own lines
    /// already split those cells correctly. Like WordGapThresholdPercent, this always overrides
    /// the text-layout engine's own setting of the same name.
    /// </summary>
    public bool RespectOcrLines { get; init; } = true;
}
