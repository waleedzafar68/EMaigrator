namespace EMaigrator.Api.Realtime;

// SignalR event payloads. Property names match the hub method names per CONTRACTS §6.
public sealed record MigrationProgressDto(string MigrationId, long Migrated, long Total, string? CurrentFolder, double MsgPerMin, string Status);

public sealed record NeedsDecisionDto(string IssueType, string Detail, string[] Options);
