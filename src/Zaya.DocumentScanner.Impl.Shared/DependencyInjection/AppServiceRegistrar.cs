using Microsoft.Extensions.DependencyInjection;
using Zaya.Logging.Services;

namespace Zaya.DocumentScanner.Impl.Shared.DependencyInjection;

public static class AppServiceRegistrar
{
    public static void Register(IServiceCollection services, ILoggingWrapper logging)
    {
        services.AddSingleton(logging);
    }
}
