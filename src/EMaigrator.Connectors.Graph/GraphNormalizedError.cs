namespace EMaigrator.Connectors.Graph;

/// <summary>
/// A Graph error normalized to a stable, credential-free signature for the Core error
/// catalog (CONTRACTS §8). Transient errors carry the honored Retry-After duration.
/// </summary>
public sealed record GraphNormalizedError(string Signature, bool IsTransient, TimeSpan? RetryAfter);
