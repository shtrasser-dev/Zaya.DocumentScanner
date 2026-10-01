using Microsoft.Extensions.DependencyInjection;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;
using Zaya.DocumentScanner.Impl.Shared.ViewModels;
using Zaya.Logging.Impl.Microsoft.Services;
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

        services.AddSingleton<IOCRService, OneOcrService>().WrapLogging<IOCRService>();
        services.AddSingleton<ITextLayoutService, ProximityTextLayoutService>().WrapLogging<ITextLayoutService>();
        services.AddSingleton<IDocumentScanService, DocumentScanService>().WrapLogging<IDocumentScanService>();
        services.AddSingleton<IRawImageFileLoader, RawImageFileLoader>().WrapLogging<IRawImageFileLoader>();
        services.AddSingleton<IRawImageBitmapService, RawImageBitmapService>().WrapLogging<IRawImageBitmapService>();
        services.AddSingleton<IDocxExportService, DocxExportService>().WrapLogging<IDocxExportService>();

        services.AddSingleton<IJsonConfigurationService, JsonConfigurationService>().WrapLogging<IJsonConfigurationService>();
        services.AddSingleton<IConfigurationPathService, ConfigurationPathService>().WrapLogging<IConfigurationPathService>();
        services.AddSingleton<IApplicationProfileService, ApplicationProfileService>().WrapLogging<IApplicationProfileService>();
        services.AddSingleton<ISettingsService, SettingsService>().WrapLogging<ISettingsService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}
