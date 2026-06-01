using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Composes the EMaigrator infrastructure adapters (persistence, secrets, rate limiter,
    /// orchestrator, observability, health checks, retention jobs) behind the Core abstractions.
    /// When <paramref name="registerBus"/> is false, the caller (host/test) owns the single
    /// MassTransit bus registration so it can attach worker consumers; the orchestrator is still registered.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool registerBus = true)
    {
        services.AddOptions<InfrastructureOptions>()
            .Bind(config.GetSection(InfrastructureOptions.SectionName))
            .ValidateOnStart();

        // ── Persistence (Task 4): DbContext factory + PostgresLedger ────────────────────────────

        // ── Secrets (Tasks 5/6): EnvelopeCipher + mode-switched ISecretStore (LocalKey | AzureKeyVault) ─

        // ── Rate limiting (Tasks 7/8): Redis multiplexer + RateLimitOptions + RedisRateLimiter ──

        // ── Messaging (Task 9): MassTransit/RabbitMQ (gated by registerBus) + IJobOrchestrator ──

        // ── Observability (Task 10): OpenTelemetry traces/metrics + Serilog with scrubbing ──────

        // ── Health checks (Task 11): Postgres + RabbitMQ + Redis ────────────────────────────────

        // ── Retention (Task 12): RetentionOptions + ICredentialPurgeHook + LogRetentionPurgeService ─

        return services;
    }
}
