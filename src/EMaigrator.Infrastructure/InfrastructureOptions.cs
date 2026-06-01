using EMaigrator.Core.Configuration;

namespace EMaigrator.Infrastructure;

/// <summary>Root options for the Infrastructure subsystem; bound from the "Infrastructure" config section.</summary>
public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    public string PostgresConnectionString { get; set; } = "";
    public string RedisConnectionString { get; set; } = "";
    public string RabbitMqConnectionString { get; set; } = "";

    public SecretStoreOptions SecretStore { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();
    public OrchestrationOptions Orchestration { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
}
