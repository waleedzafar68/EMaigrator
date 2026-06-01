namespace EMaigrator.Core.Abstractions;

/// <summary>Aggregate ledger counts for progress/results (CONTRACTS.md §4).</summary>
public sealed record LedgerCounts(long Migrated, long Skipped, long Failed, long Pending);
