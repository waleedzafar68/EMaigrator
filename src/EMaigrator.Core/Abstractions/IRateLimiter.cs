namespace EMaigrator.Core.Abstractions;

/// <summary>Distributed per-account token bucket with adaptive backoff (CONTRACTS.md §4).</summary>
public interface IRateLimiter
{
    Task<bool> TryAcquireAsync(RateLimitKey key, int tokens, CancellationToken ct);
    Task PenalizeAsync(RateLimitKey key, TimeSpan retryAfter, CancellationToken ct);
}
