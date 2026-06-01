namespace EMaigrator.Core.Preflight;

/// <summary>Estimated scope/volume for billing-quota check and ETA (CONTRACTS.md §3, DESIGN.md §14).</summary>
public sealed record MigrationEstimate(int MailboxCount, int FolderCount, long MessageCount, long TotalBytes, TimeSpan EstimatedDuration);
