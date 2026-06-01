using Testcontainers.Redis;

namespace EMaigrator.Infrastructure.IntegrationTests.Fixtures;

public sealed class RedisFixture : IAsyncLifetime
{
    public RedisContainer Container { get; } = new RedisBuilder("redis:8-alpine").Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("redis")]
public sealed class RedisCollectionFixture : ICollectionFixture<RedisFixture>;
