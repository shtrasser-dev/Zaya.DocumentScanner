namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// Sliding-window scan parameters. Defaults are the values validated against a real
/// photographed (tilted, non-flat-scan) document: dense per-pixel stepping is both far
/// slower and worse quality than a modest step combined with trust-center filtering.
/// </summary>
public sealed class DocumentScanOptions
{
    /// <summary>Recognition window height in px. Null defaults to a quarter of the page height.</summary>
    public int? WindowHeight { get; init; }

    public int Step { get; init; } = 15;

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
}
