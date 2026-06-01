using System.Security.Cryptography;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EMaigrator.Infrastructure.IntegrationTests;

/// <summary>
/// Functional verification for Plan 03: a host composed solely from <see cref="DependencyInjection.AddInfrastructure"/>
/// (pointed at the live Postgres+Redis+RabbitMQ trio) resolves and exercises all four Core seams
/// — <see cref="ILedger"/>, <see cref="ISecretStore"/>, <see cref="IRateLimiter"/>, <see cref="IJobOrchestrator"/> —
/// plus aggregate health checks, in one composed pipeline.
/// </summary>
[Collection("infra-trio")]
public sealed class AddInfrastructureEndToEndTests
{
    private readonly InfraTrioFixture _trio;

    public AddInfrastructureEndToEndTests(InfraTrioFixture trio) => _trio = trio;

    private IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Infrastructure:PostgresConnectionString"] = _trio.Postgres.ConnectionString,
        ["Infrastructure:RedisConnectionString"] = _trio.Redis.ConnectionString,
        ["Infrastructure:RabbitMqConnectionString"] = _trio.Rabbit.ConnectionString,
        ["Infrastructure:SecretStore:Mode"] = "LocalKey",
        ["Infrastructure:SecretStore:KeyRef"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Infrastructure:RateLimit:Buckets:default:RefillPerSecond"] = "0.001",
        ["Infrastructure:RateLimit:Buckets:default:Burst"] = "1",
    }).Build();

    [Fact]
    public async Task Composed_infrastructure_exercises_all_seams()
    {
        var services = new ServiceCollection();
        // registerBus: false so the test owns the single bus registration (with its capture consumer);
        // AddInfrastructure still registers ledger/secrets/rate-limiter/orchestrator/observability/health.
        services.AddInfrastructure(Config(), registerBus: false);
        services.AddMassTransit(x =>
        {
            x.AddConsumer<CaptureConsumer>();
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(_trio.Rabbit.ConnectionString));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        await using var sp = services.BuildServiceProvider(validateScopes: true);

        // Migrate the schema using the composed DbContext factory.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctxFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
            await using var ctx = ctxFactory.CreateDbContext();
            await ctx.Database.MigrateAsync();
        }

        // IBusControl is a singleton; the consumer-bearing endpoints come up on StartAsync.
        var bus = sp.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            // Ledger, secret store, and orchestrator are scoped (EF factory + MassTransit IPublishEndpoint),
            // so resolve them from a DI scope to satisfy scope validation. The rate limiter and the
            // HealthCheckService are singletons and resolve from the root provider.
            using var scope = sp.CreateScope();
            var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
            var limiter = sp.GetRequiredService<IRateLimiter>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IJobOrchestrator>();
            var health = sp.GetRequiredService<HealthCheckService>();

            // ── ILedger: mark then read back ────────────────────────────────────────────────────
            var mig = Guid.NewGuid();
            await ledger.MarkAsync(mig, "k", "INBOX", "Inbox", LedgerStatus.Migrated, null, default);
            (await ledger.IsDoneAsync(mig, "k", default)).Should().BeTrue();

            // ── ISecretStore: round-trip (ciphertext at rest, plaintext returned) ───────────────
            var secretRef = await secrets.StoreAsync(Guid.NewGuid().ToString(), "secret", default);
            (await secrets.RetrieveAsync(secretRef, default)).Should().Be("secret");

            // ── IRateLimiter: grant the single burst token, then throttle ──────────────────────
            var key = new RateLimitKey(new ProviderId("graph"), Guid.NewGuid().ToString("N"));
            (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
            (await limiter.TryAcquireAsync(key, 1, default)).Should().BeFalse();

            // ── IJobOrchestrator: enqueue StartMigration, consumed by the registered CaptureConsumer ─
            await orchestrator.EnqueueMigrationAsync(mig, default);
            var done = await Task.WhenAny(CaptureConsumer.Seen.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            done.Should().Be(CaptureConsumer.Seen.Task, "the published StartMigration should be consumed");
            (await CaptureConsumer.Seen.Task).Should().Be(mig);

            // ── Health: the composed aggregate (postgres + rabbitmq + redis) is Healthy ─────────
            (await health.CheckHealthAsync()).Status.Should().Be(HealthStatus.Healthy);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    /// <summary>Test consumer that records the first <see cref="StartMigration"/> it observes.</summary>
    public sealed class CaptureConsumer : IConsumer<StartMigration>
    {
        public static readonly TaskCompletionSource<Guid> Seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Consume(ConsumeContext<StartMigration> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Seen.TrySetResult(context.Message.MailboxMigrationId);
            return Task.CompletedTask;
        }
    }
}
