using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Zaya.DocumentScanner.Impl.Shared.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Views;

/// <summary>
/// Shows the source scan image with a live overlay while scanning runs: a translucent band for
/// the sliding OCR window's current position, and a translucent box per word recognized so far.
/// </summary>
public sealed class ScanPreviewCanvas : Panel
{
    private static readonly IBrush WindowFill = new SolidColorBrush(Color.FromArgb(70, 30, 120, 220));
    private static readonly IBrush WindowStroke = new SolidColorBrush(Color.FromArgb(200, 30, 120, 220));
    private static readonly IBrush WordFill = new SolidColorBrush(Color.FromArgb(90, 40, 190, 90));
    private static readonly IBrush WordStroke = new SolidColorBrush(Color.FromArgb(160, 30, 150, 60));

    private readonly Image _image = new();
    private readonly Canvas _overlay = new();
    private readonly Border _windowBox;
    private readonly List<Border> _wordBoxes = [];
    private double _scale = 1;

    public ScanPreviewCanvas()
    {
        Children.Add(_image);
        Children.Add(_overlay);
        _overlay.IsHitTestVisible = false;

        _windowBox = new Border
        {
            Background = WindowFill,
            BorderBrush = WindowStroke,
            BorderThickness = new Thickness(1),
            IsVisible = false,
            IsHitTestVisible = false,
        };
        _overlay.Children.Add(_windowBox);
    }

    public void SetImage(Bitmap bitmap, int sourceWidth, int sourceHeight, double maxDisplayWidth, double maxDisplayHeight)
    {
        _scale = sourceWidth <= 0 || sourceHeight <= 0
            ? 1
            : Math.Min(1.0, Math.Min(maxDisplayWidth / sourceWidth, maxDisplayHeight / sourceHeight));

        var displayWidth = sourceWidth * _scale;
        var displayHeight = sourceHeight * _scale;

        _image.Source = bitmap;
        Width = displayWidth;
        Height = displayHeight;
        _image.Width = displayWidth;
        _image.Height = displayHeight;
        _overlay.Width = displayWidth;
        _overlay.Height = displayHeight;

        ClearWords();
        _windowBox.IsVisible = false;
    }

    public void Clear()
    {
        _image.Source = null;
        Width = 0;
        Height = 0;
        ClearWords();
        _windowBox.IsVisible = false;
    }

    public void ShowWindow(int topY, int windowHeight)
    {
        _windowBox.Width = Math.Max(0, _overlay.Width);
        _windowBox.Height = Math.Max(1, windowHeight * _scale);
        Canvas.SetLeft(_windowBox, 0);
        Canvas.SetTop(_windowBox, topY * _scale);
        _windowBox.IsVisible = true;
    }

    public void HideWindow() => _windowBox.IsVisible = false;

    public void ShowWords(IReadOnlyList<ScannedWord> words)
    {
        ClearWords();
        foreach (var w in words)
        {
            var box = new Border
            {
                Background = WordFill,
                BorderBrush = WordStroke,
                BorderThickness = new Thickness(0.5),
                Width = Math.Max(1, (w.MaxX - w.MinX) * _scale),
                Height = Math.Max(1, (w.MaxY - w.MinY) * _scale),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(box, w.MinX * _scale);
            Canvas.SetTop(box, w.MinY * _scale);
            _overlay.Children.Add(box);
            _wordBoxes.Add(box);
        }
    }

    private void ClearWords()
    {
        foreach (var box in _wordBoxes)
            _overlay.Children.Remove(box);
        _wordBoxes.Clear();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(Width > 0 ? Width : 0, Height > 0 ? Height : 0);
        _image.Measure(size);
        _overlay.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = new Rect(0, 0, Width > 0 ? Width : finalSize.Width, Height > 0 ? Height : finalSize.Height);
        _image.Arrange(rect);
        _overlay.Arrange(rect);
        return finalSize;
    }
}
