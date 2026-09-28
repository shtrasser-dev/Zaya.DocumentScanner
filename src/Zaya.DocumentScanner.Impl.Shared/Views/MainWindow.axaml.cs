using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;

namespace Zaya.DocumentScanner.Impl.Shared.Views;

public partial class MainWindow : Window
{
    private readonly IDocumentScanService _scanService;
    private readonly IRawImageFileLoader _fileLoader;
    private readonly IDocxExportService _docxExportService;
    private IReadOnlyList<ScannedLine> _lastScan = [];

    public MainWindow()
    {
        _scanService = null!;
        _fileLoader = null!;
        _docxExportService = null!;
        InitializeComponent();
    }

    public MainWindow(IDocumentScanService scanService, IRawImageFileLoader fileLoader, IDocxExportService docxExportService)
    {
        _scanService = scanService;
        _fileLoader = fileLoader;
        _docxExportService = docxExportService;
        InitializeComponent();
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
            var stopwatch = Stopwatch.StartNew();
            _lastScan = await _scanService.ScanAsync(page, new DocumentScanOptions());
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
            _docxExportService.Export(_lastScan, path);
            StatusText.Text = $"Сохранено: {path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка сохранения: {ex.Message}";
        }
    }
}
