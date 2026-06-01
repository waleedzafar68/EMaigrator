namespace EMaigrator.Core.Abstractions;

/// <summary>Per-message ledger status (CONTRACTS.md §4).</summary>
public enum LedgerStatus { Pending, Migrated, Skipped, Failed }
