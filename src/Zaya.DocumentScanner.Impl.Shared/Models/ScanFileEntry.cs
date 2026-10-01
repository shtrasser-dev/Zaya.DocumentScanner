using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Zaya.DocumentScanner.Impl.Shared.Models;

/// <summary>One image queued in the main window's file table, awaiting a combined scan.</summary>
public sealed partial class ScanFileEntry : ObservableObject
{
    [ObservableProperty]
    private int _number;

    /// <summary>
    /// A downscaled preview of the source image, loaded asynchronously after the entry is added -
    /// null until then. Shown small in the file table's row and larger in that row's tooltip, so
    /// the user can tell files apart without having to guess from the filename alone.
    /// </summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public ScanFileEntry(string path)
    {
        Path = path;
    }
}
