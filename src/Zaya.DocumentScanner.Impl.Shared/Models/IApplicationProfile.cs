using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.Models;

public interface IApplicationProfile
{
    SettingDescriptorList DocumentScannerSettings { get; }
    Dictionary<string, Dictionary<string, object>> Settings { get; }
}
