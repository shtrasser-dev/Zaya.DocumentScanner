using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zaya.Logging.Impl.Microsoft.Services;
using Zaya.Logging.Impl.Services;
using Zaya.Logging.Services;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;

namespace Zaya.DocumentScanner.Impl.Shared.DependencyInjection;

public static class BootstrapServiceRegistrar
{
    public static void Register(IServiceCollection services, string? logFilePath = null)
    {
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            if (!string.IsNullOrWhiteSpace(logFilePath))
                builder.AddProvider(new FileLoggerProvider(logFilePath));
        });

        services.AddSingleton<ILoggingWrapper>(sp =>
        {
            var mel = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Zaya");
            return new LoggingWrapper(new MicrosoftExtensionsLogger(mel));
        });

        services.AddSingleton<IApplicationBootstrap, ApplicationBootstrap>();
    }
}
