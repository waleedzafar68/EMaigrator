using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The wizard's results step (Task 9). <c>GET /migrations/{id}/results</c> aggregates the per-mailbox
/// <see cref="ILedger"/> counts into counts + a source↔destination reconciliation + a needs-decision queue
/// (the failed log rows). <c>GET /migrations/{id}/audit</c> projects <see cref="MigrationLogRow"/> into
/// <see cref="AuditEntryDto"/>, omitting the subject when <see cref="Job.StoreSubjects"/> is false (privacy
/// toggle, DESIGN §10), filterable by <c>?q=</c> (subject/folder substring) and <c>?failuresOnly=</c>.
/// <c>POST /migrations/{id}/rerun</c> re-enqueues every <see cref="MailboxMigration"/> via
/// <see cref="IJobOrchestrator"/> (the worker re-scans the ledger for not-done items). Ownership is
/// enforced via the tenant-filtered <see cref="EmaigratorDbContext"/> (cross-tenant id → 404; the
/// <see cref="MigrationLogRow"/> rows are reached only through mailbox ids of the filtered job); the
/// fallback authorization policy rejects anonymous callers (401).
/// </summary>
public static class ResultsEndpoints
{
    // The resolution actions offered for every failed item in the needs-decision queue.
    private static readonly string[] DecisionOptions = ["SkipMessage", "RetryWithBackoff"];

    public static RouteGroupBuilder MapResultsEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/migrations/{id:guid}/results", ResultsAsync);
        group.MapGet("/migrations/{id:guid}/audit", AuditAsync);
        group.MapPost("/migrations/{id:guid}/rerun", RerunAsync);

        return group;
    }

    private static async Task<IResult> ResultsAsync(
        Guid id, EmaigratorDbContext db, ILedger ledger, IOptions<RetentionOptions> retention)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(retention);

        // The tenant query filter confines this lookup to the caller's tenant; a cross-tenant id is null.
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        var mbx = await db.MailboxMigrations.Where(m => m.JobId == id).ToListAsync();

        long migrated = 0, skipped = 0, failed = 0;
        foreach (var m in mbx)
        {
            var counts = await ledger.GetCountsAsync(m.Id, CancellationToken.None);
            migrated += counts.Migrated;
            skipped += counts.Skipped;
            failed += counts.Failed;
        }

        var sourceCount = migrated + skipped + failed;
        var destCount = migrated; // dest holds only the successfully written messages

        // Needs-decision queue: failed log rows are surfaced for resolution (the worker also DLQs poison
        // messages). Scoped to this job's mailboxes only.
        var mbxIds = mbx.Select(m => m.Id).ToList();
        var failedRows = await db.MigrationLogs
            .Where(l => mbxIds.Contains(l.MailboxMigrationId) && l.Status == "Failed")
            .ToListAsync();
        var needs = failedRows
            .Select(l => new NeedsDecisionItemDto(
                l.ErrorCode ?? "Unknown",
                $"{l.SourceFolder} → {l.DestFolder}",
                DecisionOptions))
            .ToList();

        // Wall-clock duration: (max FinishedAt − min StartedAt) across the job's mailboxes, but only once
        // every mailbox has both timestamps set (i.e. the whole job has started AND finished). Null while
        // any mailbox is still un-started or un-finished — duration is undefined mid-run.
        double? durationSeconds = null;
        if (mbx.Count > 0 && mbx.All(m => m.StartedAt.HasValue && m.FinishedAt.HasValue))
        {
            var minStart = mbx.Min(m => m.StartedAt!.Value);
            var maxFinish = mbx.Max(m => m.FinishedAt!.Value);
            durationSeconds = (maxFinish - minStart).TotalSeconds;
        }

        // logDeletesAt = latest MigrationLogRow.CreatedAt for this job + LogRetentionDays. Null when this
        // job has no log rows yet (nothing scheduled for deletion). One extra aggregate query over the
        // already-scoped mailbox ids.
        DateTimeOffset? logDeletesAt = null;
        var latestLog = await db.MigrationLogs
            .Where(l => mbxIds.Contains(l.MailboxMigrationId))
            .MaxAsync(l => (DateTimeOffset?)l.CreatedAt);
        if (latestLog.HasValue)
        {
            logDeletesAt = latestLog.Value.AddDays(retention.Value.LogRetentionDays);
        }

        return Results.Ok(new ResultsDto(
            new ResultCounts(migrated, skipped, failed),
            new Reconciliation(sourceCount, destCount, sourceCount == destCount + skipped + failed),
            needs,
            job.Status.ToString(),
            durationSeconds,
            logDeletesAt));
    }

    private static async Task<IResult> AuditAsync(
        Guid id, string? q, bool? failuresOnly, EmaigratorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        var mbxIds = await db.MailboxMigrations.Where(m => m.JobId == id).Select(m => m.Id).ToListAsync();

        var query = db.MigrationLogs.Where(l => mbxIds.Contains(l.MailboxMigrationId));
        if (failuresOnly == true)
        {
            query = query.Where(l => l.Status == "Failed");
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(l =>
                (l.Subject != null && l.Subject.Contains(q))
                || l.SourceFolder.Contains(q)
                || l.DestFolder.Contains(q));
        }

        var rows = await query.OrderByDescending(l => l.MessageDate).ToListAsync();
        var dtos = rows.Select(l => new AuditEntryDto(
            job.StoreSubjects ? l.Subject : null, // privacy toggle (DESIGN §10)
            l.MessageDate,
            l.SourceFolder,
            l.DestFolder,
            l.Status,
            l.ErrorCode));

        return Results.Ok(dtos);
    }

    private static async Task<IResult> RerunAsync(Guid id, EmaigratorDbContext db, IJobOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(orchestrator);

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        var mbx = await db.MailboxMigrations.Where(m => m.JobId == id).ToListAsync();
        job.Status = JobStatus.Running;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        foreach (var m in mbx)
        {
            // The worker re-scans the ledger for not-done items, so re-enqueuing every mailbox is safe.
            await orchestrator.EnqueueMigrationAsync(m.Id, CancellationToken.None);
        }

        return Results.Ok(MigrationMapper.ToDto(job, mbx));
    }
}
