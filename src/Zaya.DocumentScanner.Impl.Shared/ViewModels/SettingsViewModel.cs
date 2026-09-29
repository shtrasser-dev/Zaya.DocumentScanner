using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zaya.DocumentScanner.Impl.Shared.Constants;
using Zaya.DocumentScanner.Impl.Shared.Models;
using Zaya.DocumentScanner.Impl.Shared.Services;
using Zaya.Primitives.Settings;

namespace Zaya.DocumentScanner.Impl.Shared.ViewModels;

/// <summary>
/// Holds an editable copy of the active profile (begin/commit-edit, same pattern as
/// ScreenTranslator) plus the three fixed descriptor lists DocumentScanner renders —
/// its own scan/export settings, and the OCR/text-layout engines it always uses.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _isOpen;

    public IApplicationProfile Editing { get; private set; }

    public IReadOnlyList<SettingDescriptor> DocumentScannerDescriptors { get; }
    public IReadOnlyList<SettingDescriptor> OcrDescriptors { get; }
    public IReadOnlyList<SettingDescriptor> TextLayoutDescriptors { get; }

    public IDictionary<string, object> DocumentScannerValues => Editing.Settings[SettingsConstants.DsKey];
    public IDictionary<string, object> OcrValues => Editing.Settings[SettingsConstants.OcrKey];
    public IDictionary<string, object> TextLayoutValues => Editing.Settings[SettingsConstants.TextLayoutKey];

    public event EventHandler? Saved;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        DocumentScannerDescriptors = _settingsService.GetDocumentScannerDescriptors();
        OcrDescriptors = _settingsService.GetOcrDescriptors();
        TextLayoutDescriptors = _settingsService.GetTextLayoutDescriptors();
        Editing = _settingsService.BeginEdit();
    }

    public void Open()
    {
        Editing = _settingsService.BeginEdit();
        OnPropertyChanged(nameof(Editing));
        OnPropertyChanged(nameof(DocumentScannerValues));
        OnPropertyChanged(nameof(OcrValues));
        OnPropertyChanged(nameof(TextLayoutValues));
        IsOpen = true;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.CommitEdit(Editing);
        IsOpen = false;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
    }
}
