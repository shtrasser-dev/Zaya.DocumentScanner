using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Zaya.DocumentScanner.Impl.Shared.ViewModels;

/// <summary>
/// Drives MainWindow's shell: the settings panel's open/closed state, bound via
/// <c>IsVisible="{Binding IsSettingsOpen}"</c> - same mechanism as ScreenTranslator's own
/// MainViewModel, since a plain code-behind IsVisible toggle proved unreliable at getting
/// SizeToContent to grow/shrink the window consistently. The file table and its own actions stay
/// in MainWindow's code-behind - that part is deliberately DocumentScanner-specific.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <summary>Same "▲/▼ Настройки" pattern as ScreenTranslator's own settings toggle button.</summary>
    public string SettingsToggleText => $"{(IsSettingsOpen ? "▲" : "▼")} Настройки";

    partial void OnIsSettingsOpenChanged(bool value) => OnPropertyChanged(nameof(SettingsToggleText));

    public SettingsViewModel Settings { get; }

    public MainViewModel(SettingsViewModel settings)
    {
        Settings = settings;
        Settings.Saved += (_, _) => IsSettingsOpen = false;
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        if (IsSettingsOpen)
        {
            IsSettingsOpen = false;
            return;
        }

        Settings.Open();
        IsSettingsOpen = true;
    }
}
