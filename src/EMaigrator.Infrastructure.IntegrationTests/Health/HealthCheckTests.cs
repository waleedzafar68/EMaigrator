using EMaigrator.Infrastructure.Health;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EMaigrator.Infrastructure.IntegrationTests.Health;

[Collection("infra-trio")]
public sealed class HealthCheckTests
{
    private readonly InfraTrioFixture _trio;

    public HealthCheckTests(InfraTrioFixture trio) => _trio = trio;

    [Fact]
    public async Task All_dependencies_report_healthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmaigratorHealthChecks(new InfrastructureOptions
        {
            PostgresConnectionString = _trio.Postgres.ConnectionString,
            RabbitMqConnectionString = _trio.Rabbit.ConnectionString,
            RedisConnectionString = _trio.Redis.ConnectionString,
        });
        await using var sp = services.BuildServiceProvider();

        var svc = sp.GetRequiredService<HealthCheckService>();
        var report = await svc.CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Keys.Should().Contain(["postgres", "rabbitmq", "redis"]);
    }
}
