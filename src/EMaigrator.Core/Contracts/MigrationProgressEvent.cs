namespace EMaigrator.Core.Contracts;

/// <summary>Reconcile-only live counts; null on a normal migrate progress event.</summary>
public sealed record ReconcileProgress(int FoldersDone, int FolderTotal, long Copied, long Backfilled, long Skipped);

/// <summary>Event: live progress; Status ∈ JobStatus (CONTRACTS.md §4). <c>Reconcile</c> set only in reconcile mode.</summary>
public sealed record MigrationProgressEvent(Guid MailboxMigrationId, long Migrated, long Total,
    string? CurrentFolder, double MsgPerMin, string Status, ReconcileProgress? Reconcile = null);
