namespace EMaigrator.Core.Abstractions;

/// <summary>Date-window options for reading messages (CONTRACTS.md §2).</summary>
public sealed record ReadOptions
{
    public DateTimeOffset? Since { get; init; }
    public DateTimeOffset? Before { get; init; }
}
