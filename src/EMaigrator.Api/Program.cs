using System.Linq;
using System.Text.Json;
using EMaigrator.Api.AppConfiguration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEMaigratorApi(builder.Configuration);

var app = builder.Build();

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

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>); real endpoints arrive in later Plan 08 tasks.
public partial class Program;
