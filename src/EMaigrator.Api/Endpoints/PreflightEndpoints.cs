using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Data;
using EMaigrator.Api.Services;
using EMaigrator.Core.Preflight;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The wizard's pre-flight step (Task 7). <c>POST /migrations/{id}/preflight</c> flips the Job to
/// <see cref="JobStatus.PreFlight"/> and enqueues a background analysis, returning 202 immediately;
/// <c>GET /migrations/{id}/preflight</c> returns the stored <see cref="PreflightPlanDto"/> (404 before any
/// run). Ownership is enforced via the tenant-filtered <see cref="EmaigratorDbContext"/> (cross-tenant id
/// → 404); the fallback authorization policy rejects anonymous callers (401).
/// </summary>
public static class PreflightEndpoints
{
    private static readonly string[] ConnectionRequired = ["both connections must be configured first"];

    public static RouteGroupBuilder MapPreflightEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/migrations/{id:guid}/preflight", PostAsync);
        group.MapGet("/migrations/{id:guid}/preflight", GetAsync);

        return group;
    }

    private static async Task<IResult> PostAsync(Guid id, EmaigratorDbContext db, IBackgroundTaskQueue queue)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(queue);

        // The tenant query filter confines this lookup to the caller's tenant; a cross-tenant id is null.
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrEmpty(job.SourceConnectionRef) || string.IsNullOrEmpty(job.DestConnectionRef))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["connection"] = ConnectionRequired });
        }

        job.Status = JobStatus.PreFlight;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await queue.EnqueueAsync(async (sp, ct) =>
            await sp.GetRequiredService<IPreflightRunner>().RunAsync(id, ct));

        return Results.Accepted($"/api/v1/migrations/{id}/preflight");
    }

    private static async Task<IResult> GetAsync(Guid id, EmaigratorDbContext db, ApiSideContext side)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(side);

        // Ownership check via the filtered Job set, then read the side-stored plan.
        if (!await db.Jobs.AnyAsync(j => j.Id == id))
        {
            return Results.NotFound();
        }

        var row = await side.PreflightResults.FirstOrDefaultAsync(r => r.JobId == id);
        if (row is null)
        {
            return Results.NotFound();
        }

        var plan = JsonSerializer.Deserialize<PreflightPlan>(row.PlanJson)!;
        var dto = new PreflightPlanDto(
            plan.Issues.Select(i => new PreflightIssueDto(
                i.IssueType,
                i.AffectedPaths,
                i.RecommendedAction.ToString(),
                i.Options.Select(o => o.ToString()).ToList(),
                i.Severity.ToString(),
                i.Description)).ToList(),
            new MigrationEstimateDto(
                plan.Estimate.MailboxCount,
                plan.Estimate.FolderCount,
                plan.Estimate.MessageCount,
                plan.Estimate.TotalBytes,
                plan.Estimate.EstimatedDuration.TotalSeconds));
        return Results.Ok(dto);
    }
}
