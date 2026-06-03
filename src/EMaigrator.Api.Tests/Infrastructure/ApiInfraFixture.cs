using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real-infra trio the API composition root requires — Postgres + Redis + RabbitMQ —
/// and migrates the <see cref="EmaigratorDbContext"/> schema once, then exposes the three connection
/// strings plus a generated master key and a <see cref="BuildConfiguration"/> helper. Mirrors the
/// Workers' EmaigratorPipelineFixture (minus GreenMail). Started once per test collection so the
/// containers are shared across every API test class.
/// </summary>
public sealed class ApiInfraFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("emaigrator").WithUsername("emaigrator").WithPassword("emaigrator").Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly RabbitMqContainer _rabbit =
        new RabbitMqBuilder("rabbitmq:3.13-management-alpine").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public string RabbitMqConnectionString => _rabbit.GetConnectionString();

    /// <summary>Base64 32-byte LocalKey master key used by the secret store in tests.</summary>
    public string MasterKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _rabbit.StartAsync());

        // Migrate the schema once against the live Postgres using a throwaway DI graph (no bus).
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(), registerBus: false);
        await using var sp = services.BuildServiceProvider(validateScopes: true);
        await using var scope = sp.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
        await using var ctx = factory.CreateDbContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask());
    }

    /// <summary>
    /// In-memory configuration carrying the <c>Infrastructure:*</c> keys the composition root reads:
    /// the three connection strings, the LocalKey secret store keyed by <see cref="MasterKey"/>, and a
    /// generous default rate bucket (so nothing throttles during tests).
    /// </summary>
    public IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(ConfigurationValues()).Build();

    /// <summary>The raw <c>Infrastructure:*</c> key/value pairs, also consumed by the test factory.</summary>
    public IReadOnlyDictionary<string, string?> ConfigurationValues() => new Dictionary<string, string?>
    {
        ["Infrastructure:PostgresConnectionString"] = PostgresConnectionString,
        ["Infrastructure:RedisConnectionString"] = RedisConnectionString,
        ["Infrastructure:RabbitMqConnectionString"] = RabbitMqConnectionString,
        ["Infrastructure:SecretStore:Mode"] = "LocalKey",
        ["Infrastructure:SecretStore:KeyRef"] = MasterKey,
        ["Infrastructure:RateLimit:Buckets:default:RefillPerSecond"] = "1000",
        ["Infrastructure:RateLimit:Buckets:default:Burst"] = "1000",
    };
}

/// <summary>Shares one <see cref="ApiInfraFixture"/> (and its containers) across the API test collection.</summary>
[CollectionDefinition(Name)]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection-definition marker; the 'Collection' suffix is the conventional name for the [Collection] attribute reference.")]
public sealed class ApiCollection : ICollectionFixture<ApiInfraFixture>
{
    public const string Name = "api";
}
