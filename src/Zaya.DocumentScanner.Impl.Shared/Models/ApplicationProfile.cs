using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.Models;

public sealed class ApplicationProfile : IApplicationProfile
{
    private Dictionary<string, Dictionary<string, object>> _settings = [];
    private SettingDescriptorList? _dsSettings;

    public Dictionary<string, Dictionary<string, object>> Settings
    {
        get => _settings;
        init
        {
            _settings = value;
            EnsureSubDict(DocumentScannerSettingDescriptors.DsKey);
            EnsureSubDict(SettingsConstants.OcrKey);
            EnsureSubDict(SettingsConstants.TextLayoutKey);
        }
    }

    public SettingDescriptorList DocumentScannerSettings
    {
        get
        {
            if (_dsSettings is null)
            {
                _dsSettings = new SettingDescriptorList(DocumentScannerSettingDescriptors.All);
                _dsSettings.Bind(Settings[DocumentScannerSettingDescriptors.DsKey]);
            }
            return _dsSettings;
        }
    }

    public ApplicationProfile()
    {
        _settings = new()
        {
            [DocumentScannerSettingDescriptors.DsKey] = new(),
            [SettingsConstants.OcrKey] = new(),
            [SettingsConstants.TextLayoutKey] = new(),
        };
    }

    private void EnsureSubDict(string key)
    {
        if (!_settings.ContainsKey(key))
            _settings[key] = new();
    }
}
