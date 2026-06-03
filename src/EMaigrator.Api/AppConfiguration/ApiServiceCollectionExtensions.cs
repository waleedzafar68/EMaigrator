using System;
using EMaigrator.Infrastructure;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.AppConfiguration;

/// <summary>
/// The API composition root. Wires the Infrastructure adapters (persistence, secrets, rate limiter,
/// orchestrator, observability, health checks) plus the API-owned MassTransit bus and OpenAPI.
/// Later Plan 08 tasks layer on identity/auth, tenancy + the global query filter, SignalR, the REST
/// endpoints, CORS, and security headers — each "Modify"s this file.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddEMaigratorApi(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        // Engine seams from Infrastructure: the Postgres ledger, Redis rate limiter + backplane,
        // secret store, health checks, and observability. The API owns the single MassTransit bus
        // (so registerBus: false); the IJobOrchestrator is still registered against that bus.
        services.AddInfrastructure(config, registerBus: false);

        // The API's own MassTransit/RabbitMQ bus. No consumers yet — the orchestrator only PUBLISHES
        // pause/resume/cancel and start commands the Workers subsystem consumes; later tasks may add
        // hub-bridge consumers. ConfigureEndpoints is a no-op here until a consumer is registered.
        services.AddMassTransit(x =>
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(config["Infrastructure:RabbitMqConnectionString"] ?? ""));
                cfg.ConfigureEndpoints(ctx);
            }));

        // OpenAPI document (exposed at /openapi/* in Development by Program.cs).
        services.AddOpenApi();

        return services;
    }
}
