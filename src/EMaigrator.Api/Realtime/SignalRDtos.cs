namespace EMaigrator.Api.Realtime;

// SignalR event payloads. Property names match the hub method names per CONTRACTS §6.

/// <summary>Reconcile-only live counts on a progress push; null (omitted) on a migrate event. (CONTRACTS §6)</summary>
public sealed record ReconcileProgressDto(int FoldersDone, int FolderTotal, long Copied, long Backfilled, long Skipped);

public sealed record MigrationProgressDto(string MigrationId, long Migrated, long Total, string? CurrentFolder, double MsgPerMin, string Status, ReconcileProgressDto? Reconcile = null);

public sealed record NeedsDecisionDto(string IssueType, string Detail, string[] Options);
