namespace EMaigrator.Core.Abstractions;

/// <summary>Result of a destination write (CONTRACTS.md §2).</summary>
public sealed record WriteResult(bool Written, string? DestMessageId = null, string? ErrorCode = null);
