using System;
using EMaigrator.Api.Identity;
using EMaigrator.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

        // Api-local Identity store: its own DbContext on the SAME Postgres database as the engine,
        // but a distinct migrations-history table so the two contexts' migrations never collide.
        services.AddDbContext<AppIdentityDbContext>(o =>
            o.UseNpgsql(config["Infrastructure:PostgresConnectionString"],
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory_Identity")));

        // The authentication core (scheme provider + handler infrastructure). No schemes or middleware
        // are wired yet — Task 2 adds the JWT/cookie schemes and the auth/authorization middleware.
        // Registered here so SignInManager (below) can resolve IAuthenticationSchemeProvider.
        services.AddAuthentication();

        // IdentityCore (no cookie auth scheme yet — that arrives in Task 2). 12-char minimum password,
        // unique email, and a 5-attempt / 15-minute lockout backing the login endpoint.
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.User.RequireUniqueEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddSignInManager();

        // JWT issuance (validation middleware lands in Task 2).
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();

        // OpenAPI document (exposed at /openapi/* in Development by Program.cs).
        services.AddOpenApi();

        return services;
    }
}
