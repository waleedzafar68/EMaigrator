using System.Diagnostics.CodeAnalysis;
using Testcontainers.RabbitMq;

namespace EMaigrator.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// Spins up a real RabbitMQ broker via Testcontainers for MassTransit integration tests.
/// Shared across the "rabbitmq" collection so the (slow) container starts once per run.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    public RabbitMqContainer Container { get; } = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

// xUnit requires a [CollectionDefinition] marker type; the "Collection" suffix is the convention
// for these markers, so CA1711 (no "Collection" suffix on types) is suppressed here.
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection-definition marker; 'Collection' suffix is the framework convention.")]
[CollectionDefinition("rabbitmq")]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>;
