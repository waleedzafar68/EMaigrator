using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Reconcile;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Persistence;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Reconcile one mailbox pair against the LIVE destination. Per folder: bulk-scan the destination
/// (metadata only) and index it by normalized Message-ID, enumerate the source WITH attachment
/// metadata, and classify each message — MISSING (copy the whole message via the large-attachment-
/// capable write), INCOMPLETE (backfill ONLY the missing attachments onto the existing message), or
/// COMPLETE (skip). Never duplicates: the destination is scanned live, so an already-present message
/// is never re-copied; re-running over a matched destination performs zero writes. Bodies/attachments
/// transit memory only (DESIGN.md §6/§10). Only Graph/Exchange destinations support this.
/// </summary>
public sealed partial class ReconcileConsumer : IConsumer<ReconcileMailbox>
{
    private readonly IProviderSessionFactory _sessions;
    private readonly IRemediationPlanStore _plans;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly ILedger _ledger;
    private readonly IRateLimiter _limiter;
    private readonly StreamingCopierFactory _copierFactory;
    private readonly IMigrationStatusWriter _status;
    private readonly IJobStatusFinalizer _finalizer;
    private readonly ILogger<ReconcileConsumer> _log;

    public ReconcileConsumer(
        IProviderSessionFactory sessions,
        IRemediationPlanStore plans,
        IMigrationConnectionLookup lookup,
        ILedger ledger,
        IRateLimiter limiter,
        StreamingCopierFactory copierFactory,
        IMigrationStatusWriter status,
        IJobStatusFinalizer finalizer,
        ILogger<ReconcileConsumer> log)
    {
        _sessions = sessions;
        _plans = plans;
        _lookup = lookup;
        _ledger = ledger;
        _limiter = limiter;
        _copierFactory = copierFactory;
        _status = status;
        _finalizer = finalizer;
        _log = log;
    }

    public async Task Consume(ConsumeContext<ReconcileMailbox> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        var mid = context.Message.MailboxMigrationId;

        await _status.SetRunningAsync(mid, ct);

        await using var source = await _sessions.CreateSourceAsync(mid, ct);
        await using var dest = await _sessions.CreateDestinationAsync(mid, ct);

        if (dest is not IReconcilableDestination reconcilable)
        {
            // Gmail/IMAP destinations cannot add an attachment to an existing message → fail fast,
            // no partial writes, normalized terminal status.
            LogUnsupported(dest.Id.Value, mid);
            await _status.SetNotSupportedAsync(mid, "reconcile-unsupported-destination", ct);
            return;
        }

        var conns = await _lookup.GetAsync(mid, ct);
        if (!conns.Dest.Settings.TryGetValue("accountEmail", out var destAccount) || string.IsNullOrWhiteSpace(destAccount))
        {
            throw new InvalidOperationException(
                "Destination connection is missing the required 'accountEmail' setting used for rate-limit keying.");
        }

        var destKey = new RateLimitKey(dest.Id, destAccount);
        var copier = _copierFactory.For(_ledger, _limiter, dest);
        var approved = await _plans.GetApprovedAsync(mid, ct);
        var constraints = dest.Constraints;
        // Honor the job's optional date window so a reconcile can be scoped to a smaller slice; the
        // connectors translate Since/Before into their native query (Gmail after:/before:, Graph filter,
        // IMAP SEARCH). IncludeAttachmentMetadata drives the per-message backfill diff.
        var options = new ReadOptions
        {
            IncludeAttachmentMetadata = true,
            Since = conns.Since,
            Before = conns.Before,
        };

        // Materialize the folder set once so the per-folder progress can report a stable total.
        var folders = await source.ListFoldersAsync(ct);
        int foldersDone = 0;
        long totCopied = 0, totBackfilled = 0, totSkipped = 0;

        foreach (var folder in folders)
        {
            var destPath = FolderRemediationResolver.Resolve(folder.Path, approved, constraints);
            await dest.EnsureFolderAsync(destPath, ct);
            var folderName = folder.Path.ToString();
            LogFolderStart(mid, folderName);

            // 1) Bulk pre-scan the live destination (within the date window) → index by normalized Message-ID.
            var index = new Dictionary<string, DestMessageDigest>(StringComparer.OrdinalIgnoreCase);
            await foreach (var d in reconcilable.ScanFolderAsync(destPath, conns.Since, conns.Before, ct))
            {
                var k = IdentityKey.NormalizeMessageId(d.InternetMessageId);
                if (k is not null)
                {
                    index[k] = d;
                }
            }

            // 2) Enumerate the source (with attachment metadata) and classify each message.
            int copied = 0, backfilled = 0, skipped = 0;
            await foreach (var msg in source.ReadMessagesAsync(folder.Path, options, ct))
            {
                var key = msg.MessageId is null ? null : IdentityKey.NormalizeMessageId(msg.MessageId);
                if (key is null || !index.TryGetValue(key, out var digest))
                {
                    // MISSING → copy the whole message (large attachments handled by the hybrid write).
                    _ = await copier.CopyAsync(mid, destKey, folder.Path, destPath, msg, ct);
                    copied++;
                    continue;
                }

                var missing = AttachmentMatcher.Missing(msg.Attachments, digest.Attachments);
                if (missing.Count == 0)
                {
                    skipped++;
                    continue; // COMPLETE → skip (no write, no backfill)
                }

                // INCOMPLETE → backfill ONLY the missing attachments onto the existing message.
                _ = await reconcilable.BackfillAttachmentsAsync(destPath, digest.DestMessageId, msg, missing, ct);
                backfilled++;
            }

            LogFolderDone(mid, folderName, index.Count, copied, backfilled, skipped);

            // Publish a per-folder live progress event carrying RUNNING totals across folders. Counts +
            // folder names only — no body/attachment bytes ride the event (memory-only invariant holds).
            totCopied += copied;
            totBackfilled += backfilled;
            totSkipped += skipped;
            foldersDone++;
            await context.Publish(
                new MigrationProgressEvent(
                    mid, totCopied, totCopied + totSkipped, folderName, 0d, "Running",
                    new ReconcileProgress(foldersDone, folders.Count, totCopied, totBackfilled, totSkipped)),
                ct);
        }

        var counts = await _ledger.GetCountsAsync(mid, ct);
        await _status.SetTerminalAsync(mid, counts, ct);

        // Roll the owning job to terminal once all its mailboxes are done, and publish a terminal event
        // (Reconcile set, so MigrationCompletionConsumer ignores it) so SignalR StatusChanged fires once.
        var jobStatus = await _finalizer.FinalizeIfDoneAsync(mid, ct);
        if (jobStatus is not null)
        {
            await context.Publish(
                new MigrationProgressEvent(
                    mid, totCopied, totCopied + totSkipped, null, 0d, jobStatus.Value.ToString(),
                    new ReconcileProgress(foldersDone, folders.Count, totCopied, totBackfilled, totSkipped)),
                ct);
        }

        LogReconciled(mid);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconcile not supported on destination {Provider} for migration {Mid}.")]
    private partial void LogUnsupported(string provider, Guid mid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconcile {Mid}: scanning destination folder {Folder}...")]
    private partial void LogFolderStart(Guid mid, string folder);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconcile {Mid}: folder {Folder} done — destScanned={DestScanned} copied={Copied} backfilled={Backfilled} skipped={Skipped}.")]
    private partial void LogFolderDone(Guid mid, string folder, int destScanned, int copied, int backfilled, int skipped);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconcile complete for migration {Mid}.")]
    private partial void LogReconciled(Guid mid);
}
