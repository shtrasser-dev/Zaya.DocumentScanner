using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.Logging.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>AppData-rooted paths for DocumentScanner.</summary>
[Log(LogLevel.Debug, LogParameters = true)]
public sealed class ConfigurationPathService : IConfigurationPathService
{
    private readonly string _root;

    public ConfigurationPathService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Zaya",
            "DocumentScanner");
    }

    public string GetRootAppDirectory() => _root;

    public string GetProfilesDirectory() => Path.Combine(_root, "profiles");

    public string GetLogConfigFilePath() => Path.Combine(_root, LogConstants.ConfigFileName);

    public string GetLogsDirectory() => Path.Combine(_root, LogConstants.LogsFolderName);
}
