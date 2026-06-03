namespace EMaigrator.Core.Abstractions;

/// <summary>The idempotency ledger — single source of truth for migration state (CONTRACTS.md §4).</summary>
public interface ILedger
{
    Task<bool> IsDoneAsync(Guid mailboxMigrationId, string identityKey, CancellationToken ct);
    Task MarkAsync(Guid mailboxMigrationId, string identityKey, string sourceFolder, string destFolder,
        LedgerStatus status, string? errorCode, CancellationToken ct);
    IAsyncEnumerable<LedgerEntry> GetNotDoneAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task<LedgerCounts> GetCountsAsync(Guid mailboxMigrationId, CancellationToken ct);

    /// <summary>
    /// Idempotently seeds one Pending row per message (insert-if-absent; never downgrades a
    /// done/failed row). Seeded up front during fan-out so a later Pending==0 means "complete".
    /// </summary>
    Task SeedPendingAsync(Guid mailboxMigrationId,
        IEnumerable<(string IdentityKey, string SourceFolder, string DestFolder)> messages,
        CancellationToken ct);
}
