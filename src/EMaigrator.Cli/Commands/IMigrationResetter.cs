namespace EMaigrator.Cli.Commands;

/// <summary>
/// Reopens a finished/any migration to Running before a resume re-enqueue, so RunCommand's poll
/// waits for the re-run to complete and the completion consumer can write a fresh terminal status.
/// </summary>
public interface IMigrationResetter
{
    Task ReopenAsync(Guid mailboxMigrationId, CancellationToken ct);
}
