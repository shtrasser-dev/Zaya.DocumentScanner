using Microsoft.Extensions.DependencyInjection;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;
using Zaya.Logging.Services;
using Zaya.OCR.Impl.OneOcr;
using Zaya.OCR.Impl.ProximityTextLayout;
using Zaya.OCR.Services;

namespace Zaya.DocumentScanner.Impl.Shared.DependencyInjection;

public static class AppServiceRegistrar
{
    public static void Register(IServiceCollection services, ILoggingWrapper logging)
    {
        services.AddSingleton(logging);

        services.AddSingleton<IOCRService, OneOcrService>();
        services.AddSingleton<ITextLayoutService, ProximityTextLayoutService>();
        services.AddSingleton<IDocumentScanService, DocumentScanService>();
        services.AddSingleton<IRawImageFileLoader, RawImageFileLoader>();
    }
}
