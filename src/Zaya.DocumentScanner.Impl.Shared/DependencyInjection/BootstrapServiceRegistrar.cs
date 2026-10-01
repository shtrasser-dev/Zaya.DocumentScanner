using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zaya.Logging.Impl.Microsoft.Services;
using Zaya.Logging.Impl.Services;
using Zaya.Logging.Services;
using Zaya.DocumentScanner.Impl.Shared.Logging;
using Zaya.DocumentScanner.Impl.Shared.Logging.Impl;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;

namespace Zaya.DocumentScanner.Impl.Shared.DependencyInjection;

public static class BootstrapServiceRegistrar
{
    /// <param name="logFilePath">
    /// An additional plain-text sink for the CLI's --log option, on top of the always-on rolling
    /// file under the host's own logs directory (see <see cref="LogConfig"/>).
    /// </param>
    public static void Register(IServiceCollection services, string? logFilePath = null)
    {
        // Pre-DI: paths + log.json must load before MEL / ILoggingWrapper exist.
        var configurationPathService = new ConfigurationPathService();
        var jsonConfigurationService = new JsonConfigurationService();
        var logConfigService = new LogConfigService(jsonConfigurationService, configurationPathService);
        var logConfig = logConfigService.LoadOrCreate();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(logConfig.Level);
            if (logConfig.WriteToDebug)
                builder.AddDebug();
            if (logConfig.WriteToFile)
                builder.AddProvider(new RollingFileLoggerProvider(configurationPathService.GetLogsDirectory(), logConfig));
            if (!string.IsNullOrWhiteSpace(logFilePath))
                builder.AddProvider(new FileLoggerProvider(logFilePath));
        });

        services.AddSingleton<ILoggingWrapper>(sp =>
        {
            var mel = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Zaya");
            return new LoggingWrapper(new MicrosoftExtensionsLogger(mel));
        });

        services.AddSingleton<ILogConfigService>(logConfigService).WrapLogging<ILogConfigService>();

        services.AddSingleton<IApplicationBootstrap, ApplicationBootstrap>().WrapLogging<IApplicationBootstrap>();
    }
}
