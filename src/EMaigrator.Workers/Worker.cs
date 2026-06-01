namespace EMaigrator.Workers;

/// <summary>
/// Placeholder background service. The real queue-consuming worker logic is
/// implemented in Plan 07 (Workers); this stub only needs to compile cleanly.
/// </summary>
public sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
