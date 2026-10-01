using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.DocumentScanner.Impl.Shared.DependencyInjection;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;
using Zaya.Logging.Services;

namespace Zaya.DocumentScanner.Impl.Shared.Cli;

/// <summary>
/// Headless entry point: scans one or more images and exports a combined DOCX with no window,
/// for scripting/automation and for watching a scan's progress without driving the GUI.
/// </summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.Usage);
            return 1;
        }

        var bootstrapServices = new ServiceCollection();
        BootstrapServiceRegistrar.Register(bootstrapServices, options.LogPath);
        await using var bootstrapProvider = bootstrapServices.BuildServiceProvider();

        var bootstrap = bootstrapProvider.GetRequiredService<IApplicationBootstrap>();
        await bootstrap.RunAsync().ConfigureAwait(false);

        var logging = bootstrapProvider.GetRequiredService<ILoggingWrapper>();
        var logger = bootstrapProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Cli");

        var appServices = new ServiceCollection();
        AppServiceRegistrar.Register(appServices, logging);
        await using var appProvider = appServices.BuildServiceProvider();

        var profileService = appProvider.GetRequiredService<IApplicationProfileService>();
        var profileName = string.IsNullOrWhiteSpace(options.ProfileName)
            ? SettingsConstants.EngineDefaults.ProfileName
            : options.ProfileName;
        profileService.SetActiveProfile(profileName);
        var profile = profileService.ActiveProfile;

        logger.LogInformation("Профиль: {Profile}", profileName);
        Console.WriteLine($"Профиль: {profileName}");

        var scanService = appProvider.GetRequiredService<IDocumentScanService>();
        var fileLoader = appProvider.GetRequiredService<IRawImageFileLoader>();
        var exportService = appProvider.GetRequiredService<IDocxExportService>();

        var scanOptions = ScanOptionsFactory.BuildScanOptions(profile?.DocumentScannerSettings);
        var ocrSettings = profile?.Settings.GetValueOrDefault(SettingsConstants.OcrKey);
        var textLayoutSettings = profile?.Settings.GetValueOrDefault(SettingsConstants.TextLayoutKey);
        var exportOptions = ScanOptionsFactory.BuildExportOptions(profile?.DocumentScannerSettings);

        var pages = new List<ScannedPage>();
        for (var i = 0; i < options.ImagePaths.Count; i++)
        {
            var path = options.ImagePaths[i];
            if (!File.Exists(path))
            {
                logger.LogError("Файл не найден: {Path}", path);
                Console.Error.WriteLine($"Файл не найден: {path}");
                return 2;
            }

            Console.WriteLine($"[{i + 1}/{options.ImagePaths.Count}] {Path.GetFileName(path)}");
            logger.LogInformation("Начат файл {Index}/{Total}: {Path}", i + 1, options.ImagePaths.Count, path);

            try
            {
                using var page = fileLoader.Load(path);
                var pageMetrics = new PageMetrics(page.Width, page.Height);

                var progress = new Progress<ScanProgress>(p =>
                {
                    var percent = pageMetrics.HeightPx > 0
                        ? Math.Clamp(100 * p.WindowTopY / pageMetrics.HeightPx, 0, 100)
                        : 0;
                    Console.Write($"\r  распознаётся... {percent,3}%  (слов: {p.Words.Count})   ");
                });

                var result = await scanService.ScanAsync(page, scanOptions, ocrSettings, textLayoutSettings, progress)
                    .ConfigureAwait(false);
                Console.WriteLine();

                logger.LogInformation(
                    "Файл {Path}: {Count} строк, {RotatedCount} повёрнутых блоков",
                    path, result.Lines.Count, result.RotatedClusters.Count);
                Console.WriteLine($"  готово: {result.Lines.Count} строк, {result.RotatedClusters.Count} повёрнутых блоков");
                pages.Add(new ScannedPage(result.Lines, result.RotatedClusters, pageMetrics));
            }
            catch (Exception ex)
            {
                // The underlying service call already logged this (see [Log] on RawImageFileLoader/
                // DocumentScanService) - this is just what stops the CLI from crashing with a raw
                // stack trace on a corrupt/unreadable file.
                Console.WriteLine();
                Console.Error.WriteLine($"Ошибка на файле {Path.GetFileName(path)}: {ex.Message}");
                return 2;
            }
        }

        var outputFullPath = Path.GetFullPath(options.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        exportService.Export(pages, outputFullPath, exportOptions);

        logger.LogInformation("Сохранено: {Path}", outputFullPath);
        Console.WriteLine($"Сохранено: {outputFullPath}");
        return 0;
    }
}
