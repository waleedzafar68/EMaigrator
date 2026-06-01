using System.Diagnostics.CodeAnalysis;

namespace EMaigrator.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// Composes the Postgres, Redis, and RabbitMQ Testcontainers fixtures so a single collection can
/// exercise the full infrastructure trio (e.g. the aggregate health-check report) against real brokers.
/// The three (slow) containers start once per collection run and in parallel.
/// </summary>
public sealed class InfraTrioFixture : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();
    public RedisFixture Redis { get; } = new();
    public RabbitMqFixture Rabbit { get; } = new();

    public Task InitializeAsync() =>
        Task.WhenAll(Postgres.InitializeAsync(), Redis.InitializeAsync(), Rabbit.InitializeAsync());

    public Task DisposeAsync() =>
        Task.WhenAll(Postgres.DisposeAsync(), Redis.DisposeAsync(), Rabbit.DisposeAsync());
}

// xUnit requires a [CollectionDefinition] marker type; the "Collection" suffix is the convention
// for these markers, so CA1711 (no "Collection" suffix on types) is suppressed here.
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection-definition marker; 'Collection' suffix is the framework convention.")]
[CollectionDefinition("infra-trio")]
public sealed class InfraTrioCollection : ICollectionFixture<InfraTrioFixture>;
