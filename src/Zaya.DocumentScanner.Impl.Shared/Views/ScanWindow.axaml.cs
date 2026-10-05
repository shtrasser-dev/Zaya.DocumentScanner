using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;
using Zaya.Primitives;

namespace Zaya.DocumentScanner.Impl.Shared.Views;

/// <summary>
/// Runs the sliding-window scan over one or more queued images and shows the result - live overlay
/// on the source while it runs, then a combined document preview. Opened by <see cref="MainWindow"/>
/// once the user starts a scan.
/// </summary>
public partial class ScanWindow : Window
{
    // Fallback width for a page's preview when the scroll viewport hasn't been measured yet
    // (first page of the very first scan, before any layout pass has run). Every page after that
    // fits the actual viewport width instead.
    private const double SourcePreviewFallbackWidth = 800;

    private readonly IReadOnlyList<string> _filePaths;
    private readonly IDocumentScanService _scanService;
    private readonly IRawImageFileLoader _fileLoader;
    private readonly IRawImageBitmapService _bitmapService;
    private readonly IDocxExportService _docxExportService;
    private readonly IApplicationProfileService _applicationProfileService;
    private readonly List<ScannedPage> _pages = [];

    // One preview per source file loaded so far; the tab shows a single one at a time.
    private readonly List<ScanPreviewCanvas> _sourcePages = [];
    private int _currentSourcePage = -1;

    public ScanWindow()
    {
        _filePaths = [];
        _scanService = null!;
        _fileLoader = null!;
        _bitmapService = null!;
        _docxExportService = null!;
        _applicationProfileService = null!;
        InitializeComponent();
    }

    public ScanWindow(
        IReadOnlyList<string> filePaths,
        IDocumentScanService scanService,
        IRawImageFileLoader fileLoader,
        IRawImageBitmapService bitmapService,
        IDocxExportService docxExportService,
        IApplicationProfileService applicationProfileService)
    {
        _filePaths = filePaths;
        _scanService = scanService;
        _fileLoader = fileLoader;
        _bitmapService = bitmapService;
        _docxExportService = docxExportService;
        _applicationProfileService = applicationProfileService;
        InitializeComponent();

        Opened += async (_, _) => await RunScanAsync();
    }

    private async Task RunScanAsync()
    {
        var profile = _applicationProfileService.ActiveProfile;
        var scanOptions = ScanOptionsFactory.BuildScanOptions(profile?.DocumentScannerSettings);
        var ocrSettings = profile?.Settings.GetValueOrDefault(SettingsConstants.OcrKey);
        var textLayoutSettings = profile?.Settings.GetValueOrDefault(SettingsConstants.TextLayoutKey);
        var exportOptions = ScanOptionsFactory.BuildExportOptions(profile?.DocumentScannerSettings);

        var overallStopwatch = Stopwatch.StartNew();

        for (var i = 0; i < _filePaths.Count; i++)
        {
            var path = _filePaths[i];
            StatusText.Text = $"Файл {i + 1} из {_filePaths.Count}: {System.IO.Path.GetFileName(path)}";

            IRawImage? page = null;
            try
            {
                page = _fileLoader.Load(path);
                var pageMetrics = new PageMetrics(page.Width, page.Height);

                // Each file gets a page of its own, and the tab follows the scan: the page being
                // processed is the one on screen, whatever page the user had paged to before.
                var pageCanvas = new ScanPreviewCanvas();
                _sourcePages.Add(pageCanvas);
                ShowSourcePage(_sourcePages.Count - 1);

                var bitmap = _bitmapService.ToBitmap(page);
                var maxWidth = SourceScroll.Bounds.Width > 0 ? SourceScroll.Bounds.Width - 4 : SourcePreviewFallbackWidth;
                pageCanvas.SetImage(bitmap, page.Width, page.Height, maxWidth, double.PositiveInfinity);

                var progress = new Progress<ScanProgress>(p =>
                {
                    pageCanvas.ShowWindow(p.WindowTopY, p.WindowHeight);
                    pageCanvas.ShowWords(p.Words);
                });

                // See MainWindow's earlier fix: the OCR/layout engines are synchronous native/CPU
                // work wrapped in a Task, so this has to run off the UI thread or progress reports
                // (and the window itself) freeze until the whole scan finishes.
                var result = await Task.Run(
                    () => _scanService.ScanAsync(page, scanOptions, ocrSettings, textLayoutSettings, progress));

                pageCanvas.HideWindow();
                pageCanvas.ShowWords(result.Lines.SelectMany(l => l.Words).ToList());

                _pages.Add(new ScannedPage(result.Lines, result.RotatedClusters, pageMetrics));
                ResultPreviewHost.Content = DocumentPreviewBuilder.Build(_pages, exportOptions);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка на файле {System.IO.Path.GetFileName(path)}: {ex.Message}";
                return;
            }
            finally
            {
                page?.Dispose();
            }
        }

        overallStopwatch.Stop();
        var totalLines = _pages.Sum(p => p.Lines.Count);
        StatusText.Text = $"Готово: {_pages.Count} файл(ов), {totalLines} строк за {overallStopwatch.Elapsed:mm\\:ss}";
        SaveDocxButton.IsEnabled = _pages.Count > 0;
        ResultTabs.SelectedIndex = 1;
    }

    private void ShowSourcePage(int index)
    {
        if (index < 0 || index >= _sourcePages.Count)
            return;

        _currentSourcePage = index;
        SourcePageHost.Content = _sourcePages[index];
        SourceScroll.Offset = default;

        // Counted against every queued file, not just the pages loaded so far, so the indicator
        // doesn't read "1 из 1" while the first of several files is still being scanned.
        PageIndicator.Text = $"Страница {index + 1} из {Math.Max(_filePaths.Count, _sourcePages.Count)}";
        PrevPageButton.IsEnabled = index > 0;
        NextPageButton.IsEnabled = index < _sourcePages.Count - 1;
    }

    private void OnPrevPageClick(object? sender, RoutedEventArgs e) => ShowSourcePage(_currentSourcePage - 1);

    private void OnNextPageClick(object? sender, RoutedEventArgs e) => ShowSourcePage(_currentSourcePage + 1);

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
            var exportOptions = ScanOptionsFactory.BuildExportOptions(_applicationProfileService.ActiveProfile?.DocumentScannerSettings);
            _docxExportService.Export(_pages, path, exportOptions);
            StatusText.Text = $"Сохранено: {path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка сохранения: {ex.Message}";
        }
    }
}
