using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Startup;

/// <summary>
/// On worker startup, re-enqueue StartMigration for every not-done migration of a Running job.
/// The ledger's IsDone check makes re-fan-out idempotent — already-copied messages are skipped.
/// </summary>
public sealed partial class CrashResumeStartupService : IHostedService
{
    private readonly IInterruptedJobLookup _lookup;
    private readonly IJobOrchestrator _orchestrator;
    private readonly ILogger<CrashResumeStartupService> _log;

    public CrashResumeStartupService(
        IInterruptedJobLookup lookup,
        IJobOrchestrator orchestrator,
        ILogger<CrashResumeStartupService> log)
    {
        _lookup = lookup;
        _orchestrator = orchestrator;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var migrations = await _lookup.GetRunningMigrationsToResumeAsync(cancellationToken);
        foreach (var mid in migrations)
            await _orchestrator.EnqueueMigrationAsync(mid, cancellationToken);

        if (migrations.Count > 0)
            LogResumed(migrations.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Crash-resume re-enqueued {Count} interrupted migrations.")]
    private partial void LogResumed(int count);
}
