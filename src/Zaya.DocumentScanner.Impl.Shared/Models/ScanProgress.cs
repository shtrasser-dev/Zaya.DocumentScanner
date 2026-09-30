namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>
/// One frame of a running scan — the sliding OCR window's current position and every word
/// recognized so far — reported via <see cref="IProgress{T}"/> so the UI can overlay both live
/// on the source image.
/// </summary>
public sealed record ScanProgress(int WindowTopY, int WindowHeight, IReadOnlyList<ScannedWord> Words);
