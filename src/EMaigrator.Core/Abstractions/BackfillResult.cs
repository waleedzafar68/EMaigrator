namespace EMaigrator.Core.Abstractions;

/// <summary>Outcome of backfilling missing attachments onto one existing destination message.</summary>
public sealed record BackfillResult(int Added, int Failed, string? ErrorCode = null);
