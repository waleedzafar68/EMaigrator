namespace EMaigrator.Core.Abstractions;

/// <summary>Result of the mandatory "Test connection" gate (CONTRACTS.md §2).</summary>
public sealed record ConnectionTestResult(bool Ok, int FolderCount, long MessageCount, string? ErrorCode = null, string? RawDetail = null);
