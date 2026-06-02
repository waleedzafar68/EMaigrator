using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Copy;

/// <summary>
/// The idempotent atom: copies ONE message source→dest in-flight. Bodies transit memory only —
/// the content stream is opened, handed to the destination, and disposed; never written to disk
/// or persisted (DESIGN.md §6/§10).
/// </summary>
public sealed partial class StreamingMessageCopier
{
    private readonly ILedger _ledger;
    private readonly IRateLimiter _limiter;
    private readonly IDestinationProvider _dest;
    private readonly ILogger<StreamingMessageCopier> _log;

    public StreamingMessageCopier(
        ILedger ledger,
        IRateLimiter limiter,
        IDestinationProvider dest,
        ILogger<StreamingMessageCopier> log)
    {
        _ledger = ledger;
        _limiter = limiter;
        _dest = dest;
        _log = log;
    }

    public async Task<CopyOutcome> CopyAsync(
        Guid mailboxMigrationId,
        RateLimitKey destKey,
        FolderPath sourceFolder,
        FolderPath destFolder,
        CanonicalMessage message,
        CancellationToken ct)
    {
        // 1) Idempotency check — skip already-done messages (resume / redelivery safe).
        if (await _ledger.IsDoneAsync(mailboxMigrationId, message.IdentityKey, ct))
            return CopyOutcome.Skipped;

        // 2) Pace against the destination's token bucket.
        if (!await _limiter.TryAcquireAsync(destKey, 1, ct))
            return CopyOutcome.Throttled;

        // 3) Stream copy. Open the content stream here and guarantee it is disposed; the bytes
        //    transit memory only — never written to a field, a file, or the ledger (DESIGN.md §6/§10).
        await using var content = await message.OpenContentAsync(ct);
        WriteResult result = await _dest.WriteMessageAsync(destFolder, message, ct);

        var src = sourceFolder.ToString();
        var dst = destFolder.ToString();

        if (result.Written)
        {
            // 4) Checkpoint — per-message. No body, only identity + folders + status.
            await _ledger.MarkAsync(mailboxMigrationId, message.IdentityKey, src, dst, LedgerStatus.Migrated, null, ct);
            return CopyOutcome.Migrated;
        }

        await _ledger.MarkAsync(mailboxMigrationId, message.IdentityKey, src, dst, LedgerStatus.Failed, result.ErrorCode, ct);
        LogCopyFailed(mailboxMigrationId, dst, result.ErrorCode);
        return CopyOutcome.Failed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message copy failed mailbox={Mailbox} folder={Folder} error={Error}")]
    private partial void LogCopyFailed(Guid mailbox, string folder, string? error);
}
