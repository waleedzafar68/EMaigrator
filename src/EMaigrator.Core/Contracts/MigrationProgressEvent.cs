namespace EMaigrator.Core.Contracts;

/// <summary>Event: live progress; Status ∈ JobStatus (CONTRACTS.md §4).</summary>
public sealed record MigrationProgressEvent(Guid MailboxMigrationId, long Migrated, long Total,
    string? CurrentFolder, double MsgPerMin, string Status);
