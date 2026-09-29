using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.Services;

public interface ISettingsService
{
    /// <summary>Deep-clones the active profile into an editable copy.</summary>
    IApplicationProfile BeginEdit();

    void CommitEdit(IApplicationProfile edited);

    void CommitEditAsNew(string name, IApplicationProfile edited);

    /// <summary>DocumentScanner's own scan/export settings — see <see cref="DocumentScannerSettingDescriptors"/>.</summary>
    IReadOnlyList<SettingDescriptor> GetDocumentScannerDescriptors();

    /// <summary>OCR engine settings (fixed to <c>OneOcrService</c> — no engine picker in DocumentScanner).</summary>
    IReadOnlyList<SettingDescriptor> GetOcrDescriptors();

    /// <summary>Text layout engine settings (fixed to <c>ProximityTextLayoutService</c>).</summary>
    IReadOnlyList<SettingDescriptor> GetTextLayoutDescriptors();
}
