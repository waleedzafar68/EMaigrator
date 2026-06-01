namespace EMaigrator.Core.Configuration;

/// <summary>Token-bucket refill/burst spec for a (provider, account-class) (CONTRACTS.md §7).</summary>
public sealed record BucketSpec
{
    public double RefillPerSecond { get; init; }
    public int Burst { get; init; }
}
