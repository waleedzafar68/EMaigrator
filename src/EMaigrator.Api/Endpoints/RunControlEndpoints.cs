using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Data;
using EMaigrator.Api.Mapping;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The wizard's run-control step (Task 8). <c>POST /migrations/{id}/approve</c> persists the approved
/// per-issue-type resolutions (in <see cref="ApiSideContext"/>), flips the Job to
/// <see cref="JobStatus.Running"/> (WizardStep ≥ 5), and enqueues every <see cref="MailboxMigration"/> via
/// <see cref="IJobOrchestrator"/>. <c>POST /.../pause|resume|cancel</c> drive the orchestrator and set the
/// matching status. Ownership is enforced via the tenant-filtered <see cref="EmaigratorDbContext"/>
/// (cross-tenant id → 404); the fallback authorization policy rejects anonymous callers (401). Approve on a
/// Job that is not <see cref="JobStatus.AwaitingApproval"/> is rejected with 409; an unparseable resolution
/// action is 400.
/// </summary>
public static class RunControlEndpoints
{
    public static RouteGroupBuilder MapRunControlEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/migrations/{id:guid}/approve", ApproveAsync);
        group.MapPost("/migrations/{id:guid}/pause", PauseAsync);
        group.MapPost("/migrations/{id:guid}/resume", ResumeAsync);
        group.MapPost("/migrations/{id:guid}/cancel", CancelAsync);

        return group;
    }

    private static async Task<IResult> ApproveAsync(
        Guid id, ApproveRequest req, EmaigratorDbContext db, ApiSideContext side, IJobOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(orchestrator);

        // The tenant query filter confines this lookup to the caller's tenant; a cross-tenant id is null.
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        if (job.Status != JobStatus.AwaitingApproval)
        {
            return Results.Conflict(new { error = "migration is not awaiting approval." });
        }

        foreach (var (issueType, action) in req.Resolutions)
        {
            if (!Enum.TryParse<RemediationAction>(action, out _))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [issueType] = [$"unknown action '{action}'"],
                });
            }
        }

        // Replace any prior resolutions for this Job, then persist the approved set (Api-side store).
        var old = await side.ApprovedResolutions.Where(r => r.JobId == id).ToListAsync();
        side.ApprovedResolutions.RemoveRange(old);
        foreach (var (issueType, action) in req.Resolutions)
        {
            side.ApprovedResolutions.Add(new ApprovedResolutionRow { JobId = id, IssueType = issueType, Action = action });
        }

        await side.SaveChangesAsync();

        var mbx = await db.MailboxMigrations.Where(m => m.JobId == id).ToListAsync();
        job.Status = JobStatus.Running;
        job.WizardStep = Math.Max(job.WizardStep, 5);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var m in mbx)
        {
            m.Status = MailboxMigrationStatus.Pending;
        }

        await db.SaveChangesAsync();

        foreach (var m in mbx)
        {
            await orchestrator.EnqueueMigrationAsync(m.Id, CancellationToken.None);
        }

        return Results.Ok(MigrationMapper.ToDto(job, mbx));
    }

    private static Task<IResult> PauseAsync(Guid id, EmaigratorDbContext db, IJobOrchestrator orchestrator) =>
        ControlAsync(id, db, orchestrator, JobStatus.Paused, static (o, jobId) => o.RequestPauseAsync(jobId, CancellationToken.None));

    private static Task<IResult> ResumeAsync(Guid id, EmaigratorDbContext db, IJobOrchestrator orchestrator) =>
        ControlAsync(id, db, orchestrator, JobStatus.Running, static (o, jobId) => o.RequestResumeAsync(jobId, CancellationToken.None));

    private static Task<IResult> CancelAsync(Guid id, EmaigratorDbContext db, IJobOrchestrator orchestrator) =>
        ControlAsync(id, db, orchestrator, JobStatus.Cancelled, static (o, jobId) => o.RequestCancelAsync(jobId, CancellationToken.None));

    private static async Task<IResult> ControlAsync(
        Guid id, EmaigratorDbContext db, IJobOrchestrator orchestrator, JobStatus newStatus,
        Func<IJobOrchestrator, Guid, Task> action)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(orchestrator);

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        await action(orchestrator, id);
        job.Status = newStatus;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var mbx = await db.MailboxMigrations.Where(m => m.JobId == id).ToListAsync();
        return Results.Ok(MigrationMapper.ToDto(job, mbx));
    }
}
