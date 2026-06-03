using EMaigrator.Core.Abstractions;

namespace EMaigrator.Workers.Persistence;

/// <summary>Writes the parent MailboxMigration's lifecycle status + counts (Running / terminal).</summary>
public interface IMigrationStatusWriter
{
    Task SetRunningAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task SetTerminalAsync(Guid mailboxMigrationId, LedgerCounts counts, CancellationToken ct);
}
