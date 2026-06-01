namespace EMaigrator.Core.Diagnostics;

/// <summary>Transient = auto-retry; Structural = user decides (CONTRACTS.md §3, DESIGN.md §7).</summary>
public enum RemediationKind { Transient, Structural }
