using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>Writes scanned lines out as a .docx file, one Word paragraph per recognized paragraph.</summary>
public interface IDocxExportService
{
    void Export(IReadOnlyList<ScannedLine> lines, string path);
}
