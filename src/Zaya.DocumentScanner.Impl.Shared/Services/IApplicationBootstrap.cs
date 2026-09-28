namespace Zaya.DocumentScanner.Impl.Shared.Services;

/// <summary>Runs startup work that must complete before the main window is shown.</summary>
public interface IApplicationBootstrap
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
