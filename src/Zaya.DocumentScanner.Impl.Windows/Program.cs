using Avalonia;
using Zaya.DocumentScanner.Impl.Shared;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new SkiaOptions { UseOpacitySaveLayer = true })
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
