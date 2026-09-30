using Avalonia;
using Zaya.DocumentScanner.Impl.Shared;
using Zaya.DocumentScanner.Impl.Shared.Cli;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Any arguments mean a scripted/CLI invocation: scan headlessly, no window, exit with a code.
        if (args.Length > 0)
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new SkiaOptions { UseOpacitySaveLayer = true })
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
