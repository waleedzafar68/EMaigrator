using Testcontainers.PostgreSql;

namespace EMaigrator.Infrastructure.IntegrationTests.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("emaigrator")
        .WithUsername("emaigrator")
        .WithPassword("emaigrator")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollectionFixture : ICollectionFixture<PostgresFixture>;
