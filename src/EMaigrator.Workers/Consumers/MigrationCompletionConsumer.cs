using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Workers.Persistence;
using MassTransit;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// On each migrate progress event, re-reads the ledger; when no Pending rows remain the migration is
/// complete, so it writes the terminal MailboxMigration status (idempotent — duplicate events after the
/// first terminal write are no-ops in IMigrationStatusWriter), then asks the mode-agnostic
/// <see cref="IJobStatusFinalizer"/> to roll the owning job to terminal when all its mailboxes are done.
/// On the first such transition it publishes a terminal <see cref="MigrationProgressEvent"/> so the email
/// + SignalR <c>StatusChanged</c> fire exactly once. Reconcile events (<see cref="MigrationProgressEvent.Reconcile"/>
/// set) are ignored here — the reconcile path drives its own completion in <see cref="ReconcileConsumer"/>.
/// </summary>
public sealed class MigrationCompletionConsumer : IConsumer<MigrationProgressEvent>
{
    private readonly ILedger _ledger;
    private readonly IMigrationStatusWriter _status;
    private readonly IJobStatusFinalizer _finalizer;

    public MigrationCompletionConsumer(ILedger ledger, IMigrationStatusWriter status, IJobStatusFinalizer finalizer)
    {
        _ledger = ledger;
        _status = status;
        _finalizer = finalizer;
    }

    public async Task Consume(ConsumeContext<MigrationProgressEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Reconcile per-folder/terminal events carry running counts but are NOT ledger-Pending driven;
        // ReconcileConsumer finalizes them itself. Acting on them here would prematurely terminal the
        // mailbox mid-reconcile with partial counts.
        if (context.Message.Reconcile is not null)
        {
            return;
        }

        var ct = context.CancellationToken;
        var mid = context.Message.MailboxMigrationId;

        var counts = await _ledger.GetCountsAsync(mid, ct);
        if (counts.Pending != 0)
        {
            return;
        }

        // Known residual race (docs/KNOWN-ISSUES.md): on a `resume`, StartMigrationConsumer re-seeds
        // Pending, so a redelivered progress event can find a transient Pending==0 here and write a
        // premature terminal status — misleading status only; NO data loss/duplication (copies are
        // ledger-idempotent and SetTerminalAsync is idempotent). Do NOT "fix" by dropping the
        // seed-Pending-up-front design; the correct fix gates completion on a fan-out-complete marker.
        await _status.SetTerminalAsync(mid, counts, ct);

        // Roll the owning job to terminal once ALL its mailboxes are terminal (finalizer is idempotent and
        // gates on all-mailboxes-terminal, NOT this single ledger's Pending==0). On the first transition,
        // publish a terminal event so the completion email + SignalR StatusChanged fire exactly once.
        var jobStatus = await _finalizer.FinalizeIfDoneAsync(mid, ct);
        if (jobStatus is not null)
        {
            await context.Publish(
                new MigrationProgressEvent(
                    mid, counts.Migrated, counts.Migrated + counts.Skipped + counts.Failed,
                    null, 0d, jobStatus.Value.ToString()),
                ct);
        }
    }
}
