using EMaigrator.Core.Abstractions;

namespace EMaigrator.Workers.Persistence;

/// <summary>Writes the parent MailboxMigration's lifecycle status + counts (Running / terminal).</summary>
public interface IMigrationStatusWriter
{
    Task SetRunningAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task SetTerminalAsync(Guid mailboxMigrationId, LedgerCounts counts, CancellationToken ct);

    /// <summary>Writes a terminal Failed status with a reason — e.g. reconcile attempted against a
    /// destination that is not <c>IReconcilableDestination</c> (Gmail/IMAP). Idempotent.</summary>
    Task SetNotSupportedAsync(Guid mailboxMigrationId, string reason, CancellationToken ct);
}
