using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>Writes scanned pages out as a single .docx file, one Word paragraph per recognized paragraph, with a page break between source images.</summary>
public interface IDocxExportService
{
    void Export(IReadOnlyList<ScannedPage> pages, string path, DocxExportOptions? options = null);
}
