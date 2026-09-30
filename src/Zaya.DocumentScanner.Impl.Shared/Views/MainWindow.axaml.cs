using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.DocumentScanner.Impl.Shared.ViewModels;

namespace Zaya.DocumentScanner.Impl.Shared.Views;

public partial class MainWindow : Window
{
    // In-process only - carries the dragged ScanFileEntry reference directly, no serialization
    // needed since a drag never leaves this table.
    private static readonly DataFormat<ScanFileEntry> DragFormat =
        DataFormat.CreateInProcessFormat<ScanFileEntry>("ScanFileEntry");

    private readonly IDocumentScanService _scanService;
    private readonly IRawImageFileLoader _fileLoader;
    private readonly IDocxExportService _docxExportService;
    private readonly IApplicationProfileService _applicationProfileService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly ObservableCollection<ScanFileEntry> _files = [];

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

        FilesList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) =>
        {
            StartButton.IsEnabled = _files.Count > 0;
            RenumberFiles();
        };

        DragDrop.AddDragOverHandler(FilesList, OnRowDragOver);
        DragDrop.AddDropHandler(FilesList, OnRowDrop);
    }

    private void RenumberFiles()
    {
        for (var i = 0; i < _files.Count; i++)
            _files[i].Number = i + 1;
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
            if (path is not null && !_files.Any(f => f.Path == path))
                _files.Add(new ScanFileEntry(path));
        }
    }

    private void OnRemoveFileRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScanFileEntry entry })
            _files.Remove(entry);
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
            _docxExportService,
            _applicationProfileService);
        scanWindow.Show(this);
    }
}
