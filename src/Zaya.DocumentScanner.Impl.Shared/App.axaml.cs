using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Zaya.Logging.Services;
using Zaya.DocumentScanner.Impl.Shared.DependencyInjection;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Views;

namespace Zaya.DocumentScanner.Impl.Shared;

public partial class App : Application
{
    /// <summary>
    /// Kept alive for the app lifetime so bootstrap-phase singletons transferred into the
    /// app container (e.g. logging) are not disposed while still in use.
    /// </summary>
    private ServiceProvider? _bootstrapServiceProvider;
    private ServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var bootstrapServices = new ServiceCollection();
        BootstrapServiceRegistrar.Register(bootstrapServices);
        _bootstrapServiceProvider = bootstrapServices.BuildServiceProvider();

        var bootstrap = _bootstrapServiceProvider.GetRequiredService<IApplicationBootstrap>();
        await bootstrap.RunAsync().ConfigureAwait(true);

        var logging = _bootstrapServiceProvider.GetRequiredService<ILoggingWrapper>();

        var appServices = new ServiceCollection();
        AppServiceRegistrar.Register(appServices, logging);
        _serviceProvider = appServices.BuildServiceProvider();

        _mainWindow = new MainWindow(
            _serviceProvider.GetRequiredService<IDocumentScanService>(),
            _serviceProvider.GetRequiredService<IRawImageFileLoader>(),
            _serviceProvider.GetRequiredService<IDocxExportService>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = _mainWindow;
        }

        _mainWindow.Show();

        base.OnFrameworkInitializationCompleted();
    }
}
