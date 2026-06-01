namespace EMaigrator.Core.Configuration;

/// <summary>Worker-pool and batching knobs (CONTRACTS.md §7, ARCHITECTURE.md §8).</summary>
public sealed class OrchestrationOptions
{
    public int GlobalMaxConcurrentMigrations { get; set; } = 16;
    public int PerTenantConcurrencyCap { get; set; } = 8;
    public int PerMailboxFolderConcurrency { get; set; } = 4;
    public int BatchSize { get; set; } = 100;
    public int ConsumerPrefetch { get; set; } = 16;
    public int DlqRetryCount { get; set; } = 5;
}
