using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Api.Services;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The wizard's connection step (Task 4): <c>PUT /migrations/{id}/connection/{side}</c> stores the
/// non-secret settings on the Job and the secret via <c>ISecretStore</c> (returning a secretRef, never
/// echoed), and <c>POST /migrations/{id}/connection/{side}/test</c> builds the connector and runs its
/// mandatory connection test, mapping any provider failure through the error catalog into a stable code.
/// Both routes require authentication (the fallback policy rejects anonymous callers with 401) and run
/// through the tenant-filtered <see cref="EmaigratorDbContext"/>, so a cross-tenant id is invisible (404).
/// </summary>
public static class ConnectionEndpoints
{
    private static readonly string[] SettingsRequired = ["required"];
    private static readonly string[] UnknownAuthMethod = ["unknown auth method"];

    public static RouteGroupBuilder MapConnectionEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPut("/migrations/{id:guid}/connection/{side}", StoreAsync);
        group.MapPost("/migrations/{id:guid}/connection/{side}/test", TestAsync);

        return group;
    }

    private static async Task<IResult> StoreAsync(
        Guid id,
        string side,
        [FromBody] ConnectionRequest request,
        [FromServices] IConnectionService svc,
        [FromServices] EmaigratorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(svc);
        ArgumentNullException.ThrowIfNull(db);

        if (side is not ("from" or "to"))
        {
            return Results.BadRequest(new { error = "side must be 'from' or 'to'." });
        }

        if (request.Settings is null || request.Settings.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = SettingsRequired });
        }

        if (string.IsNullOrWhiteSpace(request.Auth) ||
            !Enum.TryParse<AuthMethod>(request.Auth, ignoreCase: true, out _))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["auth"] = UnknownAuthMethod });
        }

        try
        {
            await svc.StoreConnectionAsync(id, side, request, default);
        }
        catch (JobNotFoundException)
        {
            return Results.NotFound();
        }
        catch (BadSideException)
        {
            return Results.BadRequest(new { error = "side must be 'from' or 'to'." });
        }

        // The query filter confines this to the caller's tenant; the job exists (store succeeded).
        var job = await db.Jobs.FirstAsync(j => j.Id == id);
        var mailboxes = await db.MailboxMigrations.AsNoTracking().Where(m => m.JobId == id).ToListAsync();
        return Results.Ok(MigrationMapper.ToDto(job, mailboxes));
    }

    private static async Task<IResult> TestAsync(
        Guid id,
        string side,
        [FromServices] IConnectionService svc)
    {
        ArgumentNullException.ThrowIfNull(svc);

        try
        {
            return Results.Ok(await svc.TestConnectionAsync(id, side, default));
        }
        catch (JobNotFoundException)
        {
            return Results.NotFound();
        }
        catch (BadSideException)
        {
            return Results.BadRequest(new { error = "side must be 'from' or 'to'." });
        }
    }
}
