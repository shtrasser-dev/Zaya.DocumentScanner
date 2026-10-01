using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.Logging.Models;
using Zaya.OCR.Impl.OneOcr;
using Zaya.OCR.Impl.ProximityTextLayout;
using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

[Log(LogLevel.Debug)]
public sealed class SettingsService : ISettingsService
{
    private readonly IApplicationProfileService _applicationProfileService;

    public SettingsService(IApplicationProfileService applicationProfileService)
    {
        _applicationProfileService = applicationProfileService;
    }

    public IApplicationProfile BeginEdit()
    {
        var active = _applicationProfileService.ActiveProfile;
        if (active is null)
            return new ApplicationProfile();

        return new ApplicationProfile { Settings = DeepCloneSettings(active.Settings) };
    }

    private static Dictionary<string, Dictionary<string, object>> DeepCloneSettings(
        Dictionary<string, Dictionary<string, object>> source)
    {
        var result = new Dictionary<string, Dictionary<string, object>>(source.Count);
        foreach (var (groupKey, settings) in source)
        {
            var copy = new Dictionary<string, object>(settings.Count);
            foreach (var (key, value) in settings)
                copy[key] = DeepCloneValue(value)!;
            result[groupKey] = copy;
        }
        return result;
    }

    private static object? DeepCloneValue(object? value)
    {
        if (value is null)
            return null;
        if (value is string or bool or int or long or double or float or decimal)
            return value;
        if (value is Dictionary<string, object> dict)
        {
            var copy = new Dictionary<string, object>(dict.Count);
            foreach (var (k, v) in dict)
                copy[k] = DeepCloneValue(v)!;
            return copy;
        }
        if (value is System.Collections.IList list)
        {
            var copy = new List<object?>(list.Count);
            foreach (var item in list)
                copy.Add(DeepCloneValue(item));
            return copy;
        }
        return value;
    }

    public void CommitEdit(IApplicationProfile edited)
    {
        _applicationProfileService.Save(edited);
        _applicationProfileService.SetActiveProfile(edited);
    }

    public void CommitEditAsNew(string name, IApplicationProfile edited)
    {
        edited.Settings[DocumentScannerSettingDescriptors.DsKey][DocumentScannerSettingDescriptors.ProfileName] = name;
        _applicationProfileService.Save(edited);
        _applicationProfileService.SetActiveProfile(edited);
    }

    public IReadOnlyList<SettingDescriptor> GetDocumentScannerDescriptors() => DocumentScannerSettingDescriptors.All;

    public IReadOnlyList<SettingDescriptor> GetOcrDescriptors()
    {
        using var service = new OneOcrService();
        return service.Settings;
    }

    public IReadOnlyList<SettingDescriptor> GetTextLayoutDescriptors()
    {
        using var service = new ProximityTextLayoutService();
        return service.Settings;
    }
}
