namespace EMaigrator.Connectors.Graph;

/// <summary>Marks a transient throttling outcome carrying the honored Retry-After.</summary>
public sealed class GraphThrottledException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public GraphThrottledException(TimeSpan? retryAfter)
        : base("Graph request was throttled (HTTP 429).")
        => RetryAfter = retryAfter;
}
