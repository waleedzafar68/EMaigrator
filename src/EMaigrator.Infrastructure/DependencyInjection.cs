using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Health;
using EMaigrator.Infrastructure.Messaging;
using EMaigrator.Infrastructure.Observability;
using EMaigrator.Infrastructure.Persistence;
using EMaigrator.Infrastructure.RateLimiting;
using EMaigrator.Infrastructure.Retention;
using EMaigrator.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

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
        services.AddDbContextFactory<EmaigratorDbContext>((sp, b) =>
        {
            var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
            b.UseNpgsql(opts.PostgresConnectionString,
                npg => npg.MigrationsAssembly("EMaigrator.Infrastructure"));
        });
        services.AddScoped<EMaigrator.Core.Abstractions.ILedger, PostgresLedger>();

        // ── Secrets (Tasks 5/6): EnvelopeCipher + mode-switched ISecretStore (LocalKey | AzureKeyVault) ─
        services.AddSingleton<EnvelopeCipher>();
        services.AddSingleton<EMaigrator.Core.Abstractions.ISecretStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
            var factory = sp.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
            var cipher = sp.GetRequiredService<EnvelopeCipher>();
            var ssOptions = Options.Create(opts.SecretStore);
            IKeyWrapper wrapper = new LocalKeyWrapper(ssOptions);
            return new LocalKeyEnvelopeSecretStore(factory, wrapper, cipher);
        });

        // ── Rate limiting (Tasks 7/8): Redis multiplexer + RateLimitOptions + RedisRateLimiter ──
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        });
        services.Configure<EMaigrator.Core.Configuration.RateLimitOptions>(o =>
        {
            var opts = config.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
                       ?? new InfrastructureOptions();
            o.Buckets = opts.RateLimit.Buckets;
        });
        services.AddSingleton<EMaigrator.Core.Abstractions.IRateLimiter, RedisRateLimiter>();

        // ── Messaging (Task 9): MassTransit/RabbitMQ (gated by registerBus) + IJobOrchestrator ──
        var orchSection = config.GetSection($"{InfrastructureOptions.SectionName}:Orchestration");
        services.Configure<EMaigrator.Core.Configuration.OrchestrationOptions>(orchSection);
        if (registerBus)
        {
            services.AddEmaigratorMessaging(
                config.GetSection(InfrastructureOptions.SectionName)["RabbitMqConnectionString"] ?? "");
        }
        services.AddScoped<EMaigrator.Core.Abstractions.IJobOrchestrator, MassTransitJobOrchestrator>();

        // ── Observability (Task 10): OpenTelemetry traces/metrics + Serilog with scrubbing ──────
        services.AddEmaigratorObservability(config);

        // ── Health checks (Task 11): Postgres + RabbitMQ + Redis ────────────────────────────────
        var infraOptions = config.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
                           ?? new InfrastructureOptions();
        services.AddEmaigratorHealthChecks(infraOptions);

        // ── Retention (Task 12): RetentionOptions + ICredentialPurgeHook + LogRetentionPurgeService ─
        var retentionSection = config.GetSection($"{InfrastructureOptions.SectionName}:Retention");
        services.Configure<EMaigrator.Core.Configuration.RetentionOptions>(retentionSection);
        services.AddSingleton<ICredentialPurgeHook, CredentialPurgeHook>();
        services.AddHostedService<LogRetentionPurgeService>();

        return services;
    }
}
