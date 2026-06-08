using System.Linq;
using System.Text.Json;
using EMaigrator.Api.AppConfiguration;
using EMaigrator.Api.Data;
using EMaigrator.Api.Endpoints;
using EMaigrator.Api.Identity;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEMaigratorApi(builder.Configuration);

var app = builder.Build();

// Apply EF migrations for all three DbContexts at startup so a fresh deployment has a ready schema
// instead of 500ing every request. Each context owns a separate __EFMigrationsHistory* table: the engine
// context is created via its factory (no tenancy dependency), the Api-local Identity + side contexts are
// resolved scoped. MigrateAsync is idempotent (a no-op when current). Multi-instance rollouts that prefer
// applying migrations out-of-band can remove this, but by default a fresh DB must not 500 every request.
await using (var migrationScope = app.Services.CreateAsyncScope())
{
    var sp = migrationScope.ServiceProvider;
    var engineFactory = sp.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
    await using (var engineCtx = await engineFactory.CreateDbContextAsync())
    {
        await engineCtx.Database.MigrateAsync();
    }

    await sp.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();
    await sp.GetRequiredService<ApiSideContext>().Database.MigrateAsync();
}

// Security headers run first so every response — including errors, 404s, and short-circuited
// CORS preflights — carries nosniff/DENY/Referrer-Policy/CSP (and HSTS over HTTPS).
app.UseMiddleware<EMaigrator.Api.Security.SecurityHeadersMiddleware>();

// CORS before auth so preflight (OPTIONS) requests are answered against the configured origin policy
// without first tripping the authentication/authorization gate.
app.UseCors();

// Authentication + authorization run before endpoint mapping so the default fallback policy
// (RequireAuthenticatedUser) protects every endpoint that does not opt into .AllowAnonymous()
// (the auth endpoints and /health stay anonymous).
app.UseAuthentication();
app.UseAuthorization();

// Rate limiter after routing/auth, before endpoint mapping, so the "auth" policy (per-IP fixed window)
// applies to the register/login endpoints that opt in via .RequireRateLimiting.
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Public health endpoint over the Infrastructure-registered checks (Postgres + Redis + RabbitMQ).
// Emits { status, checks } so operators and the smoke test can read the overall + per-check status.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        }));
    },
}).AllowAnonymous();

// Versioned API surface. The fallback authorization policy (wired above) protects every route;
// the auth endpoints (register/login) opt out via .AllowAnonymous().
var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
v1.MapProviderEndpoints();
v1.MapMigrationEndpoints();
v1.MapConnectionEndpoints();
v1.MapScopeEndpoints();
v1.MapPreflightEndpoints();
v1.MapRunControlEndpoints();
v1.MapResultsEndpoints();
v1.MapReconcileEndpoints();
v1.MapReportEndpoints();

// Live migration progress hub. [Authorize]'d; the SignalR WebSocket handshake carries the bearer token
// via the access_token query string (wired into the JWT OnMessageReceived handler for /hubs paths).
app.MapHub<EMaigrator.Api.Realtime.MigrationsHub>("/hubs/migrations");

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>); real endpoints arrive in later Plan 08 tasks.
public partial class Program;
