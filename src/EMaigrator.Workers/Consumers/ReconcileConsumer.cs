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
    private readonly ILogger<ReconcileConsumer> _log;

    public ReconcileConsumer(
        IProviderSessionFactory sessions,
        IRemediationPlanStore plans,
        IMigrationConnectionLookup lookup,
        ILedger ledger,
        IRateLimiter limiter,
        StreamingCopierFactory copierFactory,
        IMigrationStatusWriter status,
        ILogger<ReconcileConsumer> log)
    {
        _sessions = sessions;
        _plans = plans;
        _lookup = lookup;
        _ledger = ledger;
        _limiter = limiter;
        _copierFactory = copierFactory;
        _status = status;
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
        var options = new ReadOptions { IncludeAttachmentMetadata = true };

        foreach (var folder in await source.ListFoldersAsync(ct))
        {
            var destPath = FolderRemediationResolver.Resolve(folder.Path, approved, constraints);
            await dest.EnsureFolderAsync(destPath, ct);

            // 1) Bulk pre-scan the live destination → index by normalized Message-ID (metadata only).
            var index = new Dictionary<string, DestMessageDigest>(StringComparer.OrdinalIgnoreCase);
            await foreach (var d in reconcilable.ScanFolderAsync(destPath, ct))
            {
                var k = IdentityKey.NormalizeMessageId(d.InternetMessageId);
                if (k is not null)
                {
                    index[k] = d;
                }
            }

            // 2) Enumerate the source (with attachment metadata) and classify each message.
            await foreach (var msg in source.ReadMessagesAsync(folder.Path, options, ct))
            {
                var key = msg.MessageId is null ? null : IdentityKey.NormalizeMessageId(msg.MessageId);
                if (key is null || !index.TryGetValue(key, out var digest))
                {
                    // MISSING → copy the whole message (large attachments handled by the hybrid write).
                    _ = await copier.CopyAsync(mid, destKey, folder.Path, destPath, msg, ct);
                    continue;
                }

                var missing = AttachmentMatcher.Missing(msg.Attachments, digest.Attachments);
                if (missing.Count == 0)
                {
                    continue; // COMPLETE → skip (no write, no backfill)
                }

                // INCOMPLETE → backfill ONLY the missing attachments onto the existing message.
                _ = await reconcilable.BackfillAttachmentsAsync(destPath, digest.DestMessageId, msg, missing, ct);
            }
        }

        var counts = await _ledger.GetCountsAsync(mid, ct);
        await _status.SetTerminalAsync(mid, counts, ct);

        LogReconciled(mid);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconcile not supported on destination {Provider} for migration {Mid}.")]
    private partial void LogUnsupported(string provider, Guid mid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconcile complete for migration {Mid}.")]
    private partial void LogReconciled(Guid mid);
}
