using System;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Stage 1: fan a mailbox pair out into per-folder tasks. Lists source folders, applies the
/// operator-approved structural remediations to compute each destination folder, ensures the
/// destination folder exists, and publishes one MigrateFolder per folder.
/// </summary>
public sealed partial class StartMigrationConsumer : IConsumer<StartMigration>
{
    private readonly IProviderSessionFactory _sessions;
    private readonly IRemediationPlanStore _plans;
    private readonly IMigrationControlGate _gate;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly ILogger<StartMigrationConsumer> _log;

    public StartMigrationConsumer(
        IProviderSessionFactory sessions,
        IRemediationPlanStore plans,
        IMigrationControlGate gate,
        IMigrationConnectionLookup lookup,
        ILogger<StartMigrationConsumer> log)
    {
        _sessions = sessions;
        _plans = plans;
        _gate = gate;
        _lookup = lookup;
        _log = log;
    }

    public async Task Consume(ConsumeContext<StartMigration> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        var mid = context.Message.MailboxMigrationId;
        var conns = await _lookup.GetAsync(mid, ct);

        var state = await _gate.GetStateAsync(conns.JobId, ct);
        if (state == MigrationControlState.Cancelled)
        {
            LogCancelled(conns.JobId);
            return;
        }

        await using var source = await _sessions.CreateSourceAsync(mid, ct);
        await using var dest = await _sessions.CreateDestinationAsync(mid, ct);

        var approved = await _plans.GetApprovedAsync(mid, ct);
        var constraints = dest.Constraints;

        var folders = await source.ListFoldersAsync(ct);
        foreach (var folder in folders)
        {
            var destPath = FolderRemediationResolver.Resolve(folder.Path, approved, constraints);
            await dest.EnsureFolderAsync(destPath, ct);
            await context.Publish(new MigrateFolder(
                mid, Guid.NewGuid(), folder.Path.ToString(), destPath.ToString()));
        }

        LogFannedOut(folders.Count, mid);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "StartMigration skipped — job {JobId} cancelled.")]
    private partial void LogCancelled(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "StartMigration fanned out {Count} folders for migration {Mid}.")]
    private partial void LogFannedOut(int count, Guid mid);
}
