namespace EMaigrator.Cli.Commands;

/// <summary>
/// Reads the current status string of a mailbox migration (mirrors MailboxMigrationStatus).
/// Implemented in the live host against the EF context; faked in unit tests.
/// </summary>
public interface IMigrationStateReader
{
    Task<string> GetStatusAsync(Guid mailboxMigrationId, CancellationToken ct);
}
