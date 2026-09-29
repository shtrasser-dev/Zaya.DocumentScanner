namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>One recognized line of text, positioned on the full page (not a single crop).</summary>
public sealed record ScannedLine(
    string Text, float TopY, float MinX, float MaxX, float Height, IReadOnlyList<ScannedWord> Words);
