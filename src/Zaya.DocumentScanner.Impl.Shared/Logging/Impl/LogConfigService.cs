using System.Diagnostics;
using Zaya.DocumentScanner.Impl.Shared.Services;

namespace Zaya.DocumentScanner.Impl.Shared.Logging.Impl;

/// <summary>
/// Loads and seeds <see cref="LogConfig"/> from <c>log.json</c> under the host data root.
/// </summary>
public sealed class LogConfigService : ILogConfigService
{
    private readonly IJsonConfigurationService _jsonConfigurationService;
    private readonly IConfigurationPathService _configurationPathService;

    public LogConfigService(IJsonConfigurationService jsonConfigurationService, IConfigurationPathService configurationPathService)
    {
        _jsonConfigurationService = jsonConfigurationService;
        _configurationPathService = configurationPathService;
    }

    public LogConfig LoadOrCreate()
    {
        Directory.CreateDirectory(_configurationPathService.GetRootAppDirectory());
        var path = _configurationPathService.GetLogConfigFilePath();

        if (File.Exists(path) && _jsonConfigurationService.TryRead<LogConfig>(path, out var loaded))
            return loaded;

        var defaults = LogConfig.CreateDefault();
        TryWrite(path, defaults);
        return defaults;
    }

    private void TryWrite(string path, LogConfig config)
    {
        try
        {
            _jsonConfigurationService.Write(path, config);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogConfig] Failed to write {path}: {ex.Message}");
        }
    }
}
