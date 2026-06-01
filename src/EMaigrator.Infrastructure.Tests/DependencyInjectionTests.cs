using EMaigrator.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Tests;

public class DependencyInjectionTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:PostgresConnectionString"] = "Host=localhost;Database=emaigrator;Username=u;Password=p",
                ["Infrastructure:RedisConnectionString"] = "localhost:6379",
                ["Infrastructure:RabbitMqConnectionString"] = "amqp://guest:guest@localhost:5672",
                ["Infrastructure:SecretStore:Mode"] = "LocalKey",
                ["Infrastructure:SecretStore:KeyRef"] = "dGVzdC1rZXktMzItYnl0ZXMtYWVzLWdjbS1rZXkhIQ==",
                ["Infrastructure:Retention:LogRetentionDays"] = "30",
            })
            .Build();

    [Fact]
    public void AddInfrastructure_binds_options_from_config()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfig());
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;

        opts.PostgresConnectionString.Should().Contain("emaigrator");
        opts.RedisConnectionString.Should().Be("localhost:6379");
        opts.RabbitMqConnectionString.Should().StartWith("amqp://");
        opts.SecretStore.Mode.Should().Be("LocalKey");
        opts.Retention.LogRetentionDays.Should().Be(30);
    }

    [Fact]
    public void AddInfrastructure_returns_same_collection_for_chaining()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfig()).Should().BeSameAs(services);
    }
}
