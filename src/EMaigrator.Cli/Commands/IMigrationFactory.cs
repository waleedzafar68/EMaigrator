using EMaigrator.Cli.Profile;

namespace EMaigrator.Cli.Commands;

/// <summary>
/// Persists a new MailboxMigration (one per MailboxPair in scope) and returns the first id to run.
/// Live impl provided by Infrastructure access in the host; faked in tests.
/// </summary>
public interface IMigrationFactory
{
    Task<Guid> CreateAsync(MigrationProfile profile, CancellationToken ct);
}
