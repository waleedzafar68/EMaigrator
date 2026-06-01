namespace EMaigrator.Core.Preflight;

/// <summary>What to migrate: single/batch, folder filters, date window (CONTRACTS.md §3).</summary>
public sealed record ScopeSpec
{
    public bool IsBatch { get; init; }
    public IReadOnlyList<MailboxPair> Pairs { get; init; } = [];
    public IReadOnlyList<string>? IncludeFolders { get; init; }
    public IReadOnlyList<string>? ExcludeFolders { get; init; }
    public DateTimeOffset? Since { get; init; }
    public DateTimeOffset? Before { get; init; }
}
