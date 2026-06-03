using System.Linq;
using System.Text.Json;
using EMaigrator.Api.AppConfiguration;
using EMaigrator.Api.Endpoints;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEMaigratorApi(builder.Configuration);

var app = builder.Build();

// Authentication + authorization run before endpoint mapping so the default fallback policy
// (RequireAuthenticatedUser) protects every endpoint that does not opt into .AllowAnonymous()
// (the auth endpoints and /health stay anonymous).
app.UseAuthentication();
app.UseAuthorization();

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
v1.MapMigrationEndpoints();
v1.MapConnectionEndpoints();
v1.MapScopeEndpoints();
v1.MapPreflightEndpoints();

// Live migration progress hub. [Authorize]'d; the SignalR WebSocket handshake carries the bearer token
// via the access_token query string (wired into the JWT OnMessageReceived handler for /hubs paths).
app.MapHub<EMaigrator.Api.Realtime.MigrationsHub>("/hubs/migrations");

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>); real endpoints arrive in later Plan 08 tasks.
public partial class Program;
