using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// DLQ handler: when a MigrateBatch exhausts retries, MassTransit produces Fault&lt;MigrateBatch&gt;.
/// We record a content-free NeedsDecisionEvent (identity keys + folder + error type only — NO body,
/// NO subject) and mark the affected messages Failed. One poison message never wedges the folder.
/// </summary>
public sealed partial class MigrateBatchFaultConsumer : IConsumer<Fault<MigrateBatch>>
{
    private readonly ILedger _ledger;
    private readonly ILogger<MigrateBatchFaultConsumer> _log;

    public MigrateBatchFaultConsumer(ILedger ledger, ILogger<MigrateBatchFaultConsumer> log)
    {
        _ledger = ledger;
        _log = log;
    }

    public async Task Consume(ConsumeContext<Fault<MigrateBatch>> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        var batch = context.Message.Message;
        var ex = context.Message.Exceptions?.FirstOrDefault();
        var errorType = ex?.ExceptionType ?? "UnknownFault";
        var errorCode = ShortCode(errorType);

        var sb = new StringBuilder();
        sb.Append("folder=").Append(batch.SourceFolder)
          .Append("; errorType=").Append(errorType)
          .Append("; refs=").Append(string.Join(",", batch.SourceMessageRefs));
        var detail = sb.ToString();

        await context.Publish(new NeedsDecisionEvent(
            batch.MailboxMigrationId, "PoisonBatch", detail, new[] { RemediationAction.SkipMessage }));

        foreach (var reference in batch.SourceMessageRefs)
        {
            // Failure isolation must not corrupt completed siblings: in a multi-message batch some
            // refs may already be Migrated/Skipped (copied by an earlier delivery before the poison
            // threw). Only park not-yet-done refs as Failed — never overwrite a terminal-success
            // ledger entry, which would silently lose an already-migrated message.
            if (await _ledger.IsDoneAsync(batch.MailboxMigrationId, reference, ct))
                continue;

            await _ledger.MarkAsync(batch.MailboxMigrationId, reference,
                batch.SourceFolder, batch.DestFolder, LedgerStatus.Failed, errorCode, ct);
        }

        LogPoisonParked(batch.MailboxMigrationId, batch.SourceFolder, batch.SourceMessageRefs.Count, errorType);
    }

    private static string ShortCode(string exceptionType)
    {
        var name = exceptionType.Split('.').Last();
        return name.EndsWith("Exception", StringComparison.Ordinal)
            ? name[..^"Exception".Length]
            : name;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poison batch parked: mailbox={Mid} folder={Folder} refs={Count} error={Error}")]
    private partial void LogPoisonParked(Guid mid, string folder, int count, string error);
}
