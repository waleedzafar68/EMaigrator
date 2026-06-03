using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EMaigrator.Api.Reporting;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The wizard's report-export step (Task 10). <c>GET /migrations/{id}/report?format=csv|pdf</c> streams a
/// downloadable migration report — total counts, wall-clock duration, and a per-destination-folder breakdown
/// — built from a completed <see cref="Job"/>'s <see cref="MailboxMigration"/> totals and its
/// <see cref="MigrationLogRow"/> rows (grouped by <see cref="MigrationLogRow.DestFolder"/>). CSV is rendered
/// via CsvHelper, PDF via QuestPDF; an unsupported <c>?format=</c> → 400. Ownership is enforced via the
/// tenant-filtered <see cref="EmaigratorDbContext"/> (cross-tenant id → 404); the fallback authorization
/// policy rejects anonymous callers (401).
/// </summary>
public static class ReportEndpoints
{
    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/migrations/{id:guid}/report", ReportAsync);
        return group;
    }

    private static async Task<IResult> ReportAsync(
        Guid id, string? format, EmaigratorDbContext db, IEnumerable<IReportBuilder> builders)
    {
        var fmt = (format ?? "csv").ToLowerInvariant();
        var builder = builders.FirstOrDefault(b =>
            string.Equals(b.Format, fmt, StringComparison.Ordinal));
        if (builder is null)
        {
            return Results.BadRequest(new { error = "format must be csv or pdf." });
        }

        // Tenant scoping: the global query filter on EmaigratorDbContext confines this lookup to the
        // caller's tenant, so a cross-tenant id simply yields no row → 404.
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        var mbx = await db.MailboxMigrations.Where(m => m.JobId == id).ToListAsync();
        var mbxIds = mbx.Select(m => m.Id).ToList();
        var logs = await db.MigrationLogs
            .Where(l => mbxIds.Contains(l.MailboxMigrationId)).ToListAsync();

        var folders = logs
            .GroupBy(l => l.DestFolder)
            .Select(g => new FolderBreakdownRow(
                g.Key,
                g.Count(x => x.Status == "Migrated"),
                g.Count(x => x.Status == "Skipped"),
                g.Count(x => x.Status == "Failed")))
            .ToList();

        var duration = mbx
            .Where(m => m.StartedAt is not null && m.FinishedAt is not null)
            .Select(m => m.FinishedAt!.Value - m.StartedAt!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

        var data = new ReportData(
            id,
            job.SourceProvider.Value,
            job.DestProvider.Value,
            job.Status.ToString(),
            mbx.Sum(m => m.MigratedCount),
            mbx.Sum(m => m.SkippedCount),
            mbx.Sum(m => m.FailedCount),
            duration,
            folders);

        var bytes = builder.Build(data);
        return Results.File(bytes, builder.ContentType, builder.FileName(id));
    }
}
