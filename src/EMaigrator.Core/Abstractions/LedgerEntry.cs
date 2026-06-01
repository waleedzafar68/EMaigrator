namespace EMaigrator.Core.Abstractions;

/// <summary>One idempotency-ledger row. No body, no subject (CONTRACTS.md §4).</summary>
public sealed record LedgerEntry(Guid MailboxMigrationId, string IdentityKey, string SourceFolder,
    string DestFolder, LedgerStatus Status, string? ErrorCode, DateTimeOffset UpdatedAt);
