using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using EMaigrator.Api.Data;
using EMaigrator.Api.Identity;
using EMaigrator.Api.Notifications;
using EMaigrator.Api.Realtime;
using EMaigrator.Api.Security;
using EMaigrator.Api.Services;
using EMaigrator.Api.Tenancy;
using EMaigrator.Connectors.Gmail;
using EMaigrator.Connectors.Graph;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

        // The API's own MassTransit/RabbitMQ bus. The orchestrator PUBLISHES pause/resume/cancel and
        // start commands the Workers subsystem consumes; the MigrationProgressBridge consumer subscribes
        // to the worker-published progress/needs-decision events and fans them out over SignalR.
        // ConfigureEndpoints binds the bridge to its receive endpoint.
        services.AddMassTransit(x =>
        {
            x.AddConsumer<MigrationProgressBridge>();
            // Task 11: the terminal-state email notifier consumes the same MigrationProgressEvent on its
            // OWN receive endpoint (ConfigureEndpoints gives each consumer one), so both fire independently
            // — the bridge fans out over SignalR, the notifier sends one idempotent email on terminal states.
            x.AddConsumer<TerminalStateNotifier>();
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(config["Infrastructure:RabbitMqConnectionString"] ?? ""));
                cfg.ConfigureEndpoints(ctx);
            });
        });

        // Api-local Identity store: its own DbContext on the SAME Postgres database as the engine,
        // but a distinct migrations-history table so the two contexts' migrations never collide.
        services.AddDbContext<AppIdentityDbContext>(o =>
            o.UseNpgsql(config["Infrastructure:PostgresConnectionString"],
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory_Identity")));

        // JWT-bearer authentication. The access token is read from the Authorization header, or — for
        // browser clients — from the HttpOnly "emaigrator.auth" cookie, or (for SignalR's WebSocket
        // handshake under /hubs) from the access_token query string. This scheme also satisfies
        // SignInManager's IAuthenticationSchemeProvider requirement, so no bare AddAuthentication() call
        // is needed. The default authorization policy below requires an authenticated user; only
        // endpoints that opt into .AllowAnonymous() (register/login, /health) stay open.
        var jwt = config.GetSection("Jwt").Get<JwtOptions>()!;
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        if (string.IsNullOrEmpty(ctx.Token) &&
                            ctx.Request.Cookies.TryGetValue("emaigrator.auth", out var cookieToken))
                        {
                            ctx.Token = cookieToken;
                        }

                        var access = ctx.Request.Query["access_token"];
                        if (string.IsNullOrEmpty(ctx.Token) && !string.IsNullOrEmpty(access) &&
                            ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            ctx.Token = access;
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorization(o =>
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // Tenancy: the per-request tenant accessor (read from the tenant_id claim) and a scoped
        // EmaigratorDbContext that reuses the engine's IDbContextFactory but sets the sentinel
        // CurrentTenantId from ICurrentTenant at creation, confining tenant-scoped reads to the caller.
        // Guid.Empty (the unauthenticated default) disables the filter, matching factory-created
        // contexts used by Workers/Infra/SecretStore. The DI container disposes the scoped context.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        services.AddScoped<EmaigratorDbContext>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
            var tenant = sp.GetRequiredService<ICurrentTenant>();
            var ctx = factory.CreateDbContext();
            ctx.CurrentTenantId = tenant.IsAuthenticated ? tenant.TenantId : Guid.Empty;
            return ctx;
        });

        // IdentityCore for password hashing + the user store. 12-char minimum password, unique email,
        // and a 5-attempt / 15-minute lockout backing the login endpoint.
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

        // The three v1 connector plugins (each TryAddEnumerable's an IProviderPlugin). The connection
        // test/preflight paths resolve the IEnumerable<IProviderPlugin> and select by descriptor.Provider.
        services.AddImapConnector();
        services.AddGraphConnector();
        services.AddGmailConnector();

        // The error catalog the connection-test path uses to map a provider failure signature into a
        // stable, credential-free code. The real Core ErrorCatalog is data-driven (its ctor takes the
        // provider rule set). The OSS core does not yet ship an assembled production rule set — see the
        // Core diagnostics tests, which construct rules inline — so it is registered with an (extensible)
        // empty rule list. Match then returns null for unmatched signatures and the service emits
        // "UNKNOWN_ERROR", which is the correct contract behavior; populating the rule set is a follow-up.
        services.AddSingleton<IErrorCatalog>(_ => new ErrorCatalog(new List<ErrorRule>()));

        // The connection wizard service: stores creds via ISecretStore + tests the connector.
        services.AddScoped<IConnectionService, ConnectionService>();

        // SignalR for the live migration hub, with a conditional Redis backplane for horizontal fan-out
        // across API nodes (enabled only when Redis:Configuration is set — off in tests, where the single
        // in-process server delivers in-memory). The notifier (scoped) is what the bridge + endpoints push
        // through. StackExchange.Redis types flow transitively via the Infrastructure reference.
        var signalR = services.AddSignalR();
        var redis = config["Redis:Configuration"];
        if (!string.IsNullOrEmpty(redis))
        {
            signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix =
                StackExchange.Redis.RedisChannel.Literal("emaigrator-signalr"));
        }

        services.AddScoped<IMigrationGroupNotifier, SignalRMigrationGroupNotifier>();

        // The bridge runs as a system/MassTransit consumer (no tenant/HttpContext), so it resolves a
        // mailbox-migration id to its owning Job id via the unfiltered DbContext factory. Singleton is
        // fine: the impl only captures the (singleton) factory and opens a short-lived context per call.
        services.AddSingleton<IMailboxJobLookup, MailboxJobLookup>();

        // Task 7: async pre-flight. The endpoints enqueue a unit of work on the in-process
        // BackgroundTaskQueue; the QueuedHostedService drains it on a background thread, creating a fresh
        // DI scope per item and resolving the scoped IPreflightRunner. The concrete BackgroundTaskQueue is
        // registered as itself (consumed by the hosted pump) AND as IBackgroundTaskQueue (consumed by
        // endpoints); tests swap only the abstraction for an inline queue. The plan is persisted to the
        // API-owned ApiSideContext — same Postgres database, its own __EFMigrationsHistory_ApiSide history
        // table so its migration coexists with the engine's and the Identity context's.
        services.AddSingleton<BackgroundTaskQueue>();
        services.AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredService<BackgroundTaskQueue>());
        services.AddHostedService<QueuedHostedService>();
        services.AddScoped<IPreflightRunner, PreflightRunner>();
        services.AddDbContext<ApiSideContext>(o =>
            o.UseNpgsql(config["Infrastructure:PostgresConnectionString"],
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory_ApiSide")));

        // Task 10: the report exporters. Both render from the provider-agnostic ReportData; the endpoint
        // resolves the IEnumerable<IReportBuilder> and selects by the lower-cased ?format= token. Singletons:
        // both are stateless (the PDF builder's QuestPDF Community license is set once in its static ctor).
        services.AddSingleton<EMaigrator.Api.Reporting.IReportBuilder, EMaigrator.Api.Reporting.CsvReportBuilder>();
        services.AddSingleton<EMaigrator.Api.Reporting.IReportBuilder, EMaigrator.Api.Reporting.PdfReportBuilder>();

        // Task 11: terminal-state email notifications. The TerminalStateNotifier consumer (registered on
        // the bus above) resolves the owning user's email + endpoint labels via the unfiltered
        // EmaigratorDbContext + UserManager, gates duplicate/concurrent terminal events with a sent-flag
        // row in ApiSideContext, and renders a credential-free template. The OSS default sender just logs;
        // hosted deployments swap in an SMTP/provider-backed IAppEmailSender.
        services.AddScoped<ISentGuard, DbSentGuard>();
        services.AddScoped<INotificationRecipientResolver, DbNotificationRecipientResolver>();
        services.AddSingleton<IAppEmailSender, LoggingEmailSender>();

        // Task 12: brute-force guard on the auth endpoints — a per-IP fixed-window limiter (policy "auth").
        services.AddEMaigratorRateLimiting();

        // Task 12: lock CORS to the configured SPA origins (empty by default; the test host injects
        // http://localhost:5173). AllowCredentials is required for the cookie/SignalR access-token flow,
        // so the policy must enumerate explicit origins (no wildcard) — an unconfigured origin gets no
        // Access-Control-Allow-Origin and is therefore blocked by the browser.
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        // OpenAPI document (exposed at /openapi/* in Development by Program.cs).
        services.AddOpenApi();

        return services;
    }
}
