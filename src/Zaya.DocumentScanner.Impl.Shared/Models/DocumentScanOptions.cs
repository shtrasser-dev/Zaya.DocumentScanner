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
}
