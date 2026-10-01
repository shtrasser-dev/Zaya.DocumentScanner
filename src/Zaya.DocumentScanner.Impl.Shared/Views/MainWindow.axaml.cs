using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.Services.Impl;
using Zaya.DocumentScanner.Impl.Shared.ViewModels;

namespace Zaya.DocumentScanner.Impl.Shared.Views;

public partial class MainWindow : Window
{
    // Long enough to look sharp in the hover preview, short enough not to waste memory holding a
    // full-resolution decode of every queued photo at once.
    private const int ThumbnailMaxDimension = 480;

    // Same mechanism as ScreenTranslator's MainWindow: collapsed hugs its content via
    // SizeToContent.Height, expanded switches to a fixed size instead of trusting SizeToContent to
    // re-measure a whole settings panel reliably. Values tuned for this window's own content
    // (nav + 4-row file table above, SettingsPanel's MaxHeight="420" tab area below).
    private const double ExpandedHeight = 820;
    private const double ExpandedMinHeight = 700;

    // In-process only - carries the dragged ScanFileEntry reference directly, no serialization
    // needed since a drag never leaves this table.
    private static readonly DataFormat<ScanFileEntry> DragFormat =
        DataFormat.CreateInProcessFormat<ScanFileEntry>("ScanFileEntry");

    private readonly IDocumentScanService _scanService;
    private readonly IRawImageFileLoader _fileLoader;
    private readonly IRawImageBitmapService _bitmapService;
    private readonly IDocxExportService _docxExportService;
    private readonly IApplicationProfileService _applicationProfileService;
    private readonly MainViewModel? _viewModel;
    private readonly ObservableCollection<ScanFileEntry> _files = [];

    public MainWindow()
    {
        _scanService = null!;
        _fileLoader = null!;
        _bitmapService = null!;
        _docxExportService = null!;
        _applicationProfileService = null!;
        InitializeComponent();
    }

    public MainWindow(
        IDocumentScanService scanService,
        IRawImageFileLoader fileLoader,
        IRawImageBitmapService bitmapService,
        IDocxExportService docxExportService,
        IApplicationProfileService applicationProfileService,
        MainViewModel mainViewModel)
    {
        _scanService = scanService;
        _fileLoader = fileLoader;
        _bitmapService = bitmapService;
        _docxExportService = docxExportService;
        _applicationProfileService = applicationProfileService;
        _viewModel = mainViewModel;
        DataContext = mainViewModel;
        InitializeComponent();

        FilesList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) =>
        {
            StartButton.IsEnabled = _files.Count > 0;
            ClearFilesButton.IsEnabled = _files.Count > 0;
            RenumberFiles();
        };

        DragDrop.AddDragOverHandler(FilesList, OnRowDragOver);
        DragDrop.AddDropHandler(FilesList, OnRowDrop);

        mainViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplySettingsExpandedSize(mainViewModel.IsSettingsOpen);

        Opened += (_, _) =>
        {
            CenterAsIfSettingsExpanded();

            // On an otherwise-idle window (nothing has changed since Show()), the very first
            // SizeToContent auto-measure lands short - it stops after Row 0 and Row 1 (the
            // settings/status row) never gets included, cutting the settings button off entirely.
            // Any later layout-invalidating change (e.g. adding a file, which flips a button's
            // IsEnabled) makes Avalonia re-measure correctly on its own. Posting one extra
            // measure/arrange pass right after the window settles reproduces that same fix
            // without waiting on the user to do something first.
            Dispatcher.UIThread.Post(() =>
            {
                InvalidateMeasure();
                MainLayoutGrid.InvalidateMeasure();
            }, DispatcherPriority.Loaded);
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen) && _viewModel is not null)
            ApplySettingsExpandedSize(_viewModel.IsSettingsOpen);
    }

    // Same mechanism as ScreenTranslator's MainWindow.ApplySettingsExpandedSize: keep the top-left
    // corner fixed and grow/shrink downward only. Collapsed hugs its content (SizeToContent.Height);
    // expanded switches to a fixed size instead of relying on SizeToContent to re-measure the whole
    // settings panel, with the settings row set to fill (*) whatever the fixed height leaves over.
    private void ApplySettingsExpandedSize(bool open)
    {
        if (MainLayoutGrid.RowDefinitions.Count > 2)
        {
            MainLayoutGrid.RowDefinitions[2].Height = open
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto;
        }

        if (open)
        {
            SizeToContent = SizeToContent.Manual;
            MinHeight = ExpandedMinHeight;
            Height = ExpandedHeight;
        }
        else
        {
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
        }
    }

    // Same technique as ScreenTranslator's CenterAsIfSettingsExpanded: center using the expanded
    // height even though the window shows collapsed at startup, so growing downward when settings
    // open later stays within space already reserved on screen instead of shifting off-center.
    private void CenterAsIfSettingsExpanded()
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen is null)
            return;

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var pixelWidth = (int)Math.Round(Width * scale);
        var pixelExpandedHeight = (int)Math.Round(ExpandedHeight * scale);

        Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - pixelWidth) / 2),
            area.Y + Math.Max(0, (area.Height - pixelExpandedHeight) / 2));
    }

    private void RenumberFiles()
    {
        for (var i = 0; i < _files.Count; i++)
            _files[i].Number = i + 1;
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите сканы документа",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null || _files.Any(f => f.Path == path))
                continue;

            var entry = new ScanFileEntry(path);
            _files.Add(entry);
            _ = LoadThumbnailAsync(entry);
        }
    }

    private async Task LoadThumbnailAsync(ScanFileEntry entry)
    {
        try
        {
            // Decoding the source file is plain CPU/IO work with no Avalonia types involved, so it
            // can safely run off the UI thread. Building the WriteableBitmap itself, though, goes
            // through Avalonia's platform render interface - every other call site in this app
            // (ScanWindow) does that on the UI thread, so this does too rather than risking
            // undefined behaviour from an unproven off-thread code path.
            using var page = await Task.Run(() => _fileLoader.Load(entry.Path));
            entry.Thumbnail = _bitmapService.ToScaledBitmap(page, ThumbnailMaxDimension);
        }
        catch (Exception ex)
        {
            // Leave Thumbnail null - the row just shows a blank preview cell for a file that
            // couldn't be decoded; scanning it for real will surface the same error again.
            StatusText.Text = $"Не удалось загрузить превью {entry.FileName}: {ex.Message}";
        }
    }

    private void OnRemoveFileRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ScanFileEntry entry })
            return;

        _files.Remove(entry);
        entry.Thumbnail?.Dispose();
    }

    private void OnClearFilesClick(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _files)
            entry.Thumbnail?.Dispose();

        _files.Clear();
    }

    private async void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ScanFileEntry entry } control)
            return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        // Marked handled so the ListBox's own click-to-select doesn't also process this same
        // press - otherwise the row it's on gets selected instead of a drag actually starting.
        e.Handled = true;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DragFormat, entry));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnRowDrop(object? sender, DragEventArgs e)
    {
        var target = (e.Source as StyledElement)?.DataContext as ScanFileEntry;
        var source = e.DataTransfer.TryGetValue(DragFormat);
        if (target is null || source is null || ReferenceEquals(source, target))
            return;

        var oldIndex = _files.IndexOf(source);
        var newIndex = _files.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0)
            return;

        _files.Move(oldIndex, newIndex);
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (_files.Count == 0)
            return;

        var scanWindow = new ScanWindow(
            _files.Select(f => f.Path).ToList(),
            _scanService,
            _fileLoader,
            _bitmapService,
            _docxExportService,
            _applicationProfileService);
        scanWindow.Show(this);
    }
}
