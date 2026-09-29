using CommunityToolkit.Mvvm.ComponentModel;
using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

public sealed class ApplicationProfileService : ObservableObject, IApplicationProfileService
{
    private readonly IJsonConfigurationService _jsonConfigurationService;
    private readonly IConfigurationPathService _configurationPathService;

    private IApplicationProfile? _activeProfile;

    public ApplicationProfileService(
        IJsonConfigurationService jsonConfigurationService,
        IConfigurationPathService configurationPathService)
    {
        _jsonConfigurationService = jsonConfigurationService;
        _configurationPathService = configurationPathService;

        Directory.CreateDirectory(_configurationPathService.GetProfilesDirectory());
    }

    public IApplicationProfile? ActiveProfile => _activeProfile;

    public void SetActiveProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = SettingsConstants.EngineDefaults.ProfileName;

        var path = ProfilePath(name);
        if (!File.Exists(path))
        {
            var names = ListProfileNames();
            var wantsDefault = string.Equals(
                name, SettingsConstants.EngineDefaults.ProfileName, StringComparison.OrdinalIgnoreCase);

            if (wantsDefault || names.Count == 0)
            {
                var defaultProfile = new ApplicationProfile();
                defaultProfile.DocumentScannerSettings.Bind(defaultProfile.Settings[DocumentScannerSettingDescriptors.DsKey]);
                defaultProfile.Settings[DocumentScannerSettingDescriptors.DsKey][DocumentScannerSettingDescriptors.ProfileName]
                    = wantsDefault ? name : SettingsConstants.EngineDefaults.ProfileName;
                Save(defaultProfile);
                _activeProfile = defaultProfile;
                OnPropertyChanged(nameof(ActiveProfile));
                return;
            }

            ActivateFromFile(ProfilePath(names[0]));
            return;
        }

        ActivateFromFile(path);
    }

    private void ActivateFromFile(string path)
    {
        var dict = _jsonConfigurationService.Read<Dictionary<string, Dictionary<string, object>>>(path);
        _activeProfile = new ApplicationProfile { Settings = dict };
        OnPropertyChanged(nameof(ActiveProfile));
    }

    public void SetActiveProfile(IApplicationProfile profile)
    {
        _activeProfile = profile;
        OnPropertyChanged(nameof(ActiveProfile));
    }

    public IApplicationProfile CreateFromDefaultTemplate(string name)
    {
        var profile = new ApplicationProfile();
        profile.Settings[DocumentScannerSettingDescriptors.DsKey][DocumentScannerSettingDescriptors.ProfileName] = name;
        return profile;
    }

    public List<string> ListProfileNames()
    {
        if (!Directory.Exists(_configurationPathService.GetProfilesDirectory()))
            return [];

        return Directory.EnumerateFiles(_configurationPathService.GetProfilesDirectory(), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();
    }

    public void Save(IApplicationProfile profile)
    {
        var name = profile.DocumentScannerSettings.GetValueAsString(DocumentScannerSettingDescriptors.ProfileName);
        _jsonConfigurationService.Write(ProfilePath(name), profile.Settings);
    }

    public void Delete(string name)
    {
        var names = ListProfileNames();
        if (names.Count <= 1)
            return;

        var path = ProfilePath(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void ExportTo(string name, string destinationPath)
    {
        var source = ProfilePath(name);
        if (!File.Exists(source))
            throw new FileNotFoundException($"Profile '{name}' not found.", source);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.Copy(source, destinationPath, overwrite: true);
    }

    public string AllocateUniqueProfileName(string preferredName)
    {
        preferredName = preferredName.Trim();
        if (string.IsNullOrWhiteSpace(preferredName))
            preferredName = SettingsConstants.EngineDefaults.ProfileName;

        var existing = new HashSet<string>(ListProfileNames(), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(preferredName))
            return preferredName;

        for (var i = 1; i <= 1000; i++)
        {
            var candidate = $"{preferredName} {i}";
            if (!existing.Contains(candidate))
                return candidate;
        }

        return $"{preferredName} {Guid.NewGuid():N}"[..Math.Min(40, preferredName.Length + 33)];
    }

    public bool TryLoadProfileFile(string path, out IApplicationProfile? profile, out string? errorMessage)
    {
        profile = null;
        errorMessage = null;
        try
        {
            var dict = _jsonConfigurationService.Read<Dictionary<string, Dictionary<string, object>>>(path);
            profile = new ApplicationProfile { Settings = dict };
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool TryRename(string oldName, string newName, out string? errorCode)
    {
        errorCode = null;
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            errorCode = ProfileConstants.ErrorEmpty;
            return false;
        }

        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return true;

        var names = ListProfileNames();
        if (names.Any(n =>
                !string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)))
        {
            errorCode = ProfileConstants.ErrorExists;
            return false;
        }

        var oldPath = ProfilePath(oldName);
        if (!File.Exists(oldPath))
        {
            errorCode = ProfileConstants.ErrorMissing;
            return false;
        }

        var dict = _jsonConfigurationService.Read<Dictionary<string, Dictionary<string, object>>>(oldPath);
        if (!dict.TryGetValue(DocumentScannerSettingDescriptors.DsKey, out var ds))
        {
            ds = new Dictionary<string, object>();
            dict[DocumentScannerSettingDescriptors.DsKey] = ds;
        }

        ds[DocumentScannerSettingDescriptors.ProfileName] = newName;

        var newPath = ProfilePath(newName);
        var samePath = string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);

        _jsonConfigurationService.Write(newPath, dict);

        if (!samePath && File.Exists(oldPath))
            File.Delete(oldPath);

        var activeName = _activeProfile?.DocumentScannerSettings
            .GetValueAsString(DocumentScannerSettingDescriptors.ProfileName);
        if (string.Equals(activeName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfile = new ApplicationProfile { Settings = dict };
            OnPropertyChanged(nameof(ActiveProfile));
        }

        return true;
    }

    private string ProfilePath(string name) =>
        Path.Combine(_configurationPathService.GetProfilesDirectory(), SanitizeFileName(name) + ".json");

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(sanitized) ? "Default" : sanitized;
    }
}
