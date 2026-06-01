using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EMaigrator.Infrastructure.Observability;

/// <summary>Shared OpenTelemetry instruments. Emission happens in Workers; this owns the names/handles.</summary>
public static class Telemetry
{
    /// <summary>The shared <see cref="ActivitySource"/>/<see cref="Meter"/> name for the whole engine.</summary>
    public const string SourceName = "EMaigrator";

    /// <summary>Activity source for distributed tracing spans emitted across the engine.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>Meter that owns all engine counters/instruments.</summary>
    public static readonly Meter Meter = new(SourceName);

    /// <summary>Count of messages successfully migrated.</summary>
    public static readonly Counter<long> MessagesMigrated =
        Meter.CreateCounter<long>("emaigrator.messages.migrated");

    /// <summary>Count of provider rate-limit (HTTP 429) responses observed.</summary>
    public static readonly Counter<long> RateLimitHits =
        Meter.CreateCounter<long>("emaigrator.ratelimit.429");

    /// <summary>Count of messages parked in the dead-letter queue.</summary>
    public static readonly Counter<long> DlqMessages =
        Meter.CreateCounter<long>("emaigrator.dlq.parked");
}
