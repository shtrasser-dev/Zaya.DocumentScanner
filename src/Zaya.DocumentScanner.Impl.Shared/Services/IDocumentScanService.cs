using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>
/// Recognizes a photographed/scanned page by dragging a fixed-height OCR window down it,
/// rather than recognizing the whole page image in one pass.
/// </summary>
public interface IDocumentScanService
{
    Task<IReadOnlyList<ScannedLine>> ScanAsync(
        IRawImage page,
        DocumentScanOptions options,
        IReadOnlyDictionary<string, object>? ocrSettings = null,
        IReadOnlyDictionary<string, object>? textLayoutSettings = null,
        CancellationToken cancellationToken = default);
}
