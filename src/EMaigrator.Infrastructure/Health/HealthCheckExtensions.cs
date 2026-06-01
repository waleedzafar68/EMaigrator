using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace EMaigrator.Infrastructure.Health;

/// <summary>
/// Registers the EMaigrator readiness health checks (Postgres, RabbitMQ, Redis) so a host can expose
/// a <c>/health/ready</c> endpoint filtered by the <c>"ready"</c> tag.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds readiness health checks named <c>postgres</c>, <c>rabbitmq</c>, and <c>redis</c> (all tagged
    /// <c>"ready"</c>) built from the supplied <paramref name="options"/> connection strings.
    /// </summary>
    /// <remarks>
    /// The RabbitMQ check needs an <see cref="IConnection"/>. AspNetCore.HealthChecks.Rabbitmq 9.0.0
    /// (on RabbitMQ.Client 7.x) dropped the connection-string overload and instead consumes a connection
    /// factory delegate, so we register a single <see cref="IConnection"/> here. It is registered
    /// <em>lazily</em> — the factory only opens the broker connection on first resolution — so calling
    /// this from <c>AddInfrastructure</c> and building the provider never touches a live broker.
    /// </remarks>
    /// <param name="services">The service collection to add the checks to.</param>
    /// <param name="options">Infrastructure options carrying the dependency connection strings.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddEmaigratorHealthChecks(this IServiceCollection services, InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var rabbitConnectionString = options.RabbitMqConnectionString;

        // Lazy singleton: the connection is only opened the first time it is resolved (i.e. when the
        // rabbitmq health check actually runs), never at registration / BuildServiceProvider time.
        services.AddSingleton<IConnection>(_ =>
        {
            var factory = new ConnectionFactory { Uri = new Uri(rabbitConnectionString) };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        services.AddHealthChecks()
            .AddNpgSql(options.PostgresConnectionString, name: "postgres", tags: ["ready"])
            .AddRabbitMQ(
                sp => sp.GetRequiredService<IConnection>(),
                name: "rabbitmq",
                tags: ["ready"])
            .AddRedis(options.RedisConnectionString, name: "redis", tags: ["ready"]);

        return services;
    }
}
