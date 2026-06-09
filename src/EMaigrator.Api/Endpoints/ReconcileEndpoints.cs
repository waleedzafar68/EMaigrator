using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Mapping;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// <c>POST /migrations/{id}/reconcile</c> marks the job as a reconcile run (<see cref="JobMode.Reconcile"/>),
/// flips it to Running, and enqueues a <c>ReconcileMailbox</c> per <see cref="MailboxMigration"/> via the
/// single <see cref="IJobOrchestrator"/> publish seam (mirrors <c>rerun</c>). The worker's ReconcileConsumer
/// diffs the source against the LIVE destination and copies/backfills/skips per message. Ownership is
/// enforced via the tenant-filtered <see cref="EmaigratorDbContext"/> (cross-tenant/unknown id → 404); the
/// fallback authorization policy rejects anonymous callers (401).
/// </summary>
public static class ReconcileEndpoints
{
    public static RouteGroupBuilder MapReconcileEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/migrations/{id:guid}/reconcile", ReconcileAsync);

        return group;
    }

    private static async Task<IResult> ReconcileAsync(Guid id, EmaigratorDbContext db, IJobOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(orchestrator);

        // The tenant query filter confines this lookup to the caller's tenant; a cross-tenant id is null.
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        // A run is already in flight — a second concurrent reconcile would interleave scans/copies of
        // the same mailbox (safe but wasteful, ~1h of folder scanning each) and confuse run accounting.
        if (job.Status == JobStatus.Running)
        {
            return Results.Conflict(new
            {
                error = "A run is already in progress for this migration. Wait for it to finish (or pause/cancel) before starting a reconcile.",
            });
        }

        var mbx = await db.MailboxMigrations.Where(m => m.JobId == id).ToListAsync();

        job.Mode = JobMode.Reconcile;
        job.Status = JobStatus.Running;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        // Reset each mailbox row for a fresh run: the status writer only advances Pending rows and never
        // overwrites a terminal row, so without this a re-run leaves stale counts/duration/status and the
        // finalizer rolls up the PREVIOUS run's outcome forever.
        foreach (var m in mbx)
        {
            m.Status = MailboxMigrationStatus.Pending;
            m.StartedAt = null;
            m.FinishedAt = null;
        }

        await db.SaveChangesAsync();

        foreach (var m in mbx)
        {
            await orchestrator.EnqueueReconcileAsync(m.Id, CancellationToken.None);
        }

        return Results.Ok(MigrationMapper.ToDto(job, mbx));
    }
}
