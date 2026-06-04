using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Workers.Persistence;
using MassTransit;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// On each progress event, re-reads the ledger; when no Pending rows remain the migration is
/// complete, so it writes the terminal MailboxMigration status (idempotent — duplicate events
/// after the first terminal write are no-ops in IMigrationStatusWriter).
/// </summary>
public sealed class MigrationCompletionConsumer : IConsumer<MigrationProgressEvent>
{
    private readonly ILedger _ledger;
    private readonly IMigrationStatusWriter _status;

    public MigrationCompletionConsumer(ILedger ledger, IMigrationStatusWriter status)
    {
        _ledger = ledger;
        _status = status;
    }

    public async Task Consume(ConsumeContext<MigrationProgressEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        var mid = context.Message.MailboxMigrationId;

        var counts = await _ledger.GetCountsAsync(mid, ct);
        if (counts.Pending == 0)
        {
            // Known residual race (docs/KNOWN-ISSUES.md): on a `resume`, StartMigrationConsumer re-seeds
            // Pending, so a redelivered progress event can find a transient Pending==0 here and write a
            // premature terminal status — misleading status only; NO data loss/duplication (copies are
            // ledger-idempotent and SetTerminalAsync is idempotent). Do NOT "fix" by dropping the
            // seed-Pending-up-front design; the correct fix gates completion on a fan-out-complete marker.
            await _status.SetTerminalAsync(mid, counts, ct);
        }
    }
}
