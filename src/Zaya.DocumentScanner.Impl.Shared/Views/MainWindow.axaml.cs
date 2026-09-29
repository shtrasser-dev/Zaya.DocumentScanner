using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;
using Zaya.DocumentScanner.Impl.Shared.ViewModels;
using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.Views;

public partial class MainWindow : Window
{
    private readonly IDocumentScanService _scanService;
    private readonly IRawImageFileLoader _fileLoader;
    private readonly IDocxExportService _docxExportService;
    private readonly IApplicationProfileService _applicationProfileService;
    private readonly SettingsViewModel _settingsViewModel;
    private IReadOnlyList<ScannedLine> _lastScan = [];
    private PageMetrics _lastPage = new(0, 0);

    public MainWindow()
    {
        _scanService = null!;
        _fileLoader = null!;
        _docxExportService = null!;
        _applicationProfileService = null!;
        _settingsViewModel = null!;
        InitializeComponent();
    }

    public MainWindow(
        IDocumentScanService scanService,
        IRawImageFileLoader fileLoader,
        IDocxExportService docxExportService,
        IApplicationProfileService applicationProfileService,
        SettingsViewModel settingsViewModel)
    {
        _scanService = scanService;
        _fileLoader = fileLoader;
        _docxExportService = docxExportService;
        _applicationProfileService = applicationProfileService;
        _settingsViewModel = settingsViewModel;
        InitializeComponent();

        SettingsPanelControl.DataContext = _settingsViewModel;
        _settingsViewModel.Saved += (_, _) => SettingsPanelControl.IsVisible = false;
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (SettingsPanelControl.IsVisible)
        {
            SettingsPanelControl.IsVisible = false;
            return;
        }

        _settingsViewModel.Open();
        SettingsPanelControl.IsVisible = true;
    }

    private async void OnScanClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите скан документа",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null)
            return;

        ScanButton.IsEnabled = false;
        SaveDocxButton.IsEnabled = false;
        StatusText.Text = "Распознаю...";

        try
        {
            var page = _fileLoader.Load(path);
            _lastPage = new PageMetrics(page.Width, page.Height);

            var profile = _applicationProfileService.ActiveProfile;
            var scanOptions = BuildScanOptions(profile?.DocumentScannerSettings);
            var ocrSettings = profile?.Settings.GetValueOrDefault(SettingsConstants.OcrKey);
            var textLayoutSettings = profile?.Settings.GetValueOrDefault(SettingsConstants.TextLayoutKey);

            var stopwatch = Stopwatch.StartNew();
            _lastScan = await _scanService.ScanAsync(page, scanOptions, ocrSettings, textLayoutSettings);
            stopwatch.Stop();

            ResultText.Text = DocumentTextFormatter.ToPlainText(_lastScan);
            StatusText.Text = $"{_lastScan.Count} строк за {stopwatch.Elapsed:mm\\:ss}";
            SaveDocxButton.IsEnabled = _lastScan.Count > 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private async void OnSaveDocxClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить как DOCX",
            SuggestedFileName = "document.docx",
            FileTypeChoices = [new FilePickerFileType("Word") { Patterns = ["*.docx"] }],
        });

        var path = file?.TryGetLocalPath();
        if (path is null)
            return;

        try
        {
            var exportOptions = BuildExportOptions(_applicationProfileService.ActiveProfile?.DocumentScannerSettings);
            _docxExportService.Export(_lastScan, _lastPage, path, exportOptions);
            StatusText.Text = $"Сохранено: {path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private static DocumentScanOptions BuildScanOptions(SettingDescriptorList? ds)
    {
        if (ds is null)
            return new DocumentScanOptions();

        return new DocumentScanOptions
        {
            Step = ds.GetValueAsInt(SettingsConstants.Step),
            MarginMultiplier = ds.GetValueAsInt(SettingsConstants.MarginMultiplierPercent) / 100f,
            MissingFrameGrace = ds.GetValueAsInt(SettingsConstants.MissingFrameGrace),
            WordGapThresholdPercent = ds.GetValueAsInt(SettingsConstants.WordGapThresholdPercent),
        };
    }

    private static DocxExportOptions BuildExportOptions(SettingDescriptorList? ds)
    {
        if (ds is null)
            return new DocxExportOptions();

        return new DocxExportOptions
        {
            PageSize = ds.GetValueAsString(SettingsConstants.PageSize),
            FontFamily = ds.GetValueAsString(SettingsConstants.DocxFontFamily),
            FontSizeMinPt = ds.GetValueAsInt(SettingsConstants.FontSizeMinPt),
            FontSizeMaxPt = ds.GetValueAsInt(SettingsConstants.FontSizeMaxPt),
            LayoutTolerancePercent = ds.GetValueAsInt(SettingsConstants.LayoutTolerancePercent),
            BoldDetectionEnabled = ds.GetValueAsBool(SettingsConstants.BoldDetectionEnabled),
        };
    }
}
