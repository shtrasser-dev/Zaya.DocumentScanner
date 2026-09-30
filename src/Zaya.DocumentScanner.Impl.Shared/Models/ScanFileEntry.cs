using CommunityToolkit.Mvvm.ComponentModel;

namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>One image queued in the main window's file table, awaiting a combined scan.</summary>
public sealed partial class ScanFileEntry : ObservableObject
{
    [ObservableProperty]
    private int _number;

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public ScanFileEntry(string path)
    {
        Path = path;
    }
}
