using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Persistence;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Stage 1: fan a mailbox pair out into per-folder tasks. Marks the migration Running, lists source
/// folders, applies the operator-approved structural remediations to compute each destination folder,
/// ensures the destination folder exists, and seeds one Pending ledger row per source message UP FRONT
/// (so the ledger's Pending count only ever decreases — a later Pending==0 unambiguously means "all
/// work done", eliminating the distributed completion race). An empty mailbox is written terminal here.
/// </summary>
public sealed partial class StartMigrationConsumer : IConsumer<StartMigration>
{
    private readonly IProviderSessionFactory _sessions;
    private readonly IRemediationPlanStore _plans;
    private readonly IMigrationControlGate _gate;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly IMessageRefLister _lister;
    private readonly ILedger _ledger;
    private readonly IMigrationStatusWriter _status;
    private readonly ILogger<StartMigrationConsumer> _log;

    public StartMigrationConsumer(
        IProviderSessionFactory sessions,
        IRemediationPlanStore plans,
        IMigrationControlGate gate,
        IMigrationConnectionLookup lookup,
        IMessageRefLister lister,
        ILedger ledger,
        IMigrationStatusWriter status,
        ILogger<StartMigrationConsumer> log)
    {
        _sessions = sessions;
        _plans = plans;
        _gate = gate;
        _lookup = lookup;
        _lister = lister;
        _ledger = ledger;
        _status = status;
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

        await _status.SetRunningAsync(mid, ct);

        await using var source = await _sessions.CreateSourceAsync(mid, ct);
        await using var dest = await _sessions.CreateDestinationAsync(mid, ct);

        var approved = await _plans.GetApprovedAsync(mid, ct);
        var constraints = dest.Constraints;

        var folders = await source.ListFoldersAsync(ct);
        long totalSeeded = 0;
        foreach (var folder in folders)
        {
            var destPath = FolderRemediationResolver.Resolve(folder.Path, approved, constraints);
            await dest.EnsureFolderAsync(destPath, ct);

            // Seed Pending for every message in this folder BEFORE publishing the folder task, so that
            // once batches start completing the ledger's Pending count decreases monotonically to zero
            // — a later Pending==0 unambiguously means "all work done" (no completion race).
            var src = folder.Path.ToString();
            var dst = destPath.ToString();
            var seeds = new List<(string IdentityKey, string SourceFolder, string DestFolder)>();
            await foreach (var reference in _lister.ListRefsAsync(source, folder.Path, ct))
            {
                seeds.Add((reference, src, dst));
            }

            if (seeds.Count > 0)
            {
                await _ledger.SeedPendingAsync(mid, seeds, ct);
                totalSeeded += seeds.Count;
            }

            await context.Publish(new MigrateFolder(mid, Guid.NewGuid(), src, dst));
        }

        if (totalSeeded == 0)
        {
            // Nothing to migrate (empty mailbox / all folders empty): no batch will ever fire a
            // progress event, so write the terminal status now instead of waiting for completion.
            var counts = await _ledger.GetCountsAsync(mid, ct);
            await _status.SetTerminalAsync(mid, counts, ct);
        }

        LogFannedOut(folders.Count, mid);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "StartMigration skipped — job {JobId} cancelled.")]
    private partial void LogCancelled(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "StartMigration fanned out {Count} folders for migration {Mid}.")]
    private partial void LogFannedOut(int count, Guid mid);
}
