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

    public MainWindow()
    {
        _scanService = null!;
        _fileLoader = null!;
        InitializeComponent();
    }

    public MainWindow(IDocumentScanService scanService, IRawImageFileLoader fileLoader)
    {
        _scanService = scanService;
        _fileLoader = fileLoader;
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

        ((Button)sender!).IsEnabled = false;
        StatusText.Text = "Распознаю...";

        try
        {
            var page = _fileLoader.Load(path);
            var stopwatch = Stopwatch.StartNew();
            var lines = await _scanService.ScanAsync(page, new DocumentScanOptions());
            stopwatch.Stop();

            ResultText.Text = DocumentTextFormatter.ToPlainText(lines);
            StatusText.Text = $"{lines.Count} строк за {stopwatch.Elapsed:mm\\:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            ((Button)sender!).IsEnabled = true;
        }
    }
}
