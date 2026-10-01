using Zaya.Logging.Models;

namespace Zaya.DocumentScanner.Impl.Shared.Services.Impl;

/// <summary>No startup work yet — plugin loading and update checks will land here later.</summary>
[Log(LogLevel.Debug)]
public sealed class ApplicationBootstrap : IApplicationBootstrap
{
    public Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
