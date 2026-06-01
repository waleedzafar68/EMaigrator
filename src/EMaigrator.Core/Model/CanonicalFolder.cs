namespace EMaigrator.Core.Model;

/// <summary>A canonical folder with an estimated count and optional special-use flag (CONTRACTS.md §1).</summary>
public sealed record CanonicalFolder(FolderPath Path, long EstimatedMessageCount, MessageFlags? SpecialUse = null);
