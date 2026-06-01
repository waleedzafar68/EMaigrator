namespace EMaigrator.Core.Configuration;

/// <summary>Metadata-log retention window (CONTRACTS.md §7, DESIGN.md §10).</summary>
public sealed class RetentionOptions
{
    public int LogRetentionDays { get; set; } = 30;
}
