using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Api.Tenancy;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The tenant-scoped migrations surface (Task 3): create a Draft, list/read with tenant isolation,
/// discard (draft) or cancel, and set the source/destination endpoints during the wizard. Every route
/// requires authentication (the fallback authorization policy rejects anonymous callers with 401) and
/// reads/writes through the per-request tenant-filtered <see cref="EmaigratorDbContext"/>, so a
/// cross-tenant id is simply invisible (→ 404).
/// </summary>
public static class MigrationEndpoints
{
    public static RouteGroupBuilder MapMigrationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var migrations = group.MapGroup("/migrations");

        migrations.MapPost("/", CreateAsync);
        migrations.MapGet("/", ListAsync);
        migrations.MapGet("/{id:guid}", GetAsync);
        migrations.MapDelete("/{id:guid}", DeleteAsync);
        migrations.MapPatch("/{id:guid}/endpoints", SetEndpointsAsync);

        return group;
    }

    private static async Task<IResult> CreateAsync(
        [FromServices] EmaigratorDbContext db,
        [FromServices] ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(tenant);

        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            SourceProvider = new ProviderId(""),
            DestProvider = new ProviderId(""),
            Status = JobStatus.Draft,
            WizardStep = 1,
            StoreSubjects = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var dto = MigrationMapper.ToDto(job, Array.Empty<MailboxMigration>());
        return Results.Created($"/api/v1/migrations/{job.Id}", dto);
    }

    private static async Task<IResult> ListAsync(
        [FromServices] EmaigratorDbContext db,
        [FromQuery] string? status,
        [FromQuery] string? q)
    {
        ArgumentNullException.ThrowIfNull(db);

        // The query filter already confines this to the caller's tenant. Tenant job counts are small,
        // so we materialize then filter in memory — provider columns are value-converted, which makes a
        // server-side Contains on them brittle to translate.
        var jobs = await db.Jobs.AsNoTracking().ToListAsync();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, ignoreCase: true, out var jobStatus))
        {
            jobs = jobs.Where(j => j.Status == jobStatus).ToList();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            jobs = jobs.Where(j =>
                    j.SourceProvider.Value.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    j.DestProvider.Value.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Batch-load every mailbox for the page in ONE query, then group in memory — avoids the N+1 of
        // a per-job query. Tenant-safe: MailboxMigration has no tenant filter, but jobIds come from the
        // already tenant-filtered Jobs query above, so only the caller's mailboxes are ever loaded.
        var jobIds = jobs.Select(j => j.Id).ToList();
        var mailboxesByJob = (await db.MailboxMigrations.AsNoTracking()
                .Where(m => jobIds.Contains(m.JobId)).ToListAsync())
            .GroupBy(m => m.JobId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<MailboxMigration>)g.ToList());
        var result = jobs
            .Select(j => MigrationMapper.ToDto(j, mailboxesByJob.GetValueOrDefault(j.Id, Array.Empty<MailboxMigration>())))
            .ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        [FromServices] EmaigratorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        // The global query filter confines this lookup to the caller's tenant → cross-tenant ids return null (404).
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        var mailboxes = await LoadMailboxesAsync(db, job.Id);
        return Results.Ok(MigrationMapper.ToDto(job, mailboxes));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        [FromServices] EmaigratorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        // The global query filter confines this lookup to the caller's tenant → cross-tenant ids return null (404).
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        if (job.Status == JobStatus.Draft)
        {
            db.Jobs.Remove(job);
        }
        else
        {
            job.Status = JobStatus.Cancelled;
            job.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> SetEndpointsAsync(
        Guid id,
        [FromBody] SetEndpointsRequest request,
        [FromServices] EmaigratorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);

        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(request);
        if (!Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true))
        {
            return Results.ValidationProblem(ToErrorDictionary(validationResults));
        }

        // The global query filter confines this lookup to the caller's tenant → cross-tenant ids return null (404).
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        job.SourceProvider = new ProviderId(request.From);
        job.DestProvider = new ProviderId(request.To);
        job.WizardStep = Math.Max(job.WizardStep, 2);
        job.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        var mailboxes = await LoadMailboxesAsync(db, job.Id);
        return Results.Ok(MigrationMapper.ToDto(job, mailboxes));
    }

    // MailboxMigration has no tenant query filter, so it is only ever loaded by a JobId that was
    // already obtained from a tenant-filtered Job lookup.
    private static async Task<IReadOnlyCollection<MailboxMigration>> LoadMailboxesAsync(
        EmaigratorDbContext db, Guid jobId) =>
        await db.MailboxMigrations.AsNoTracking().Where(m => m.JobId == jobId).ToListAsync();

    private static Dictionary<string, string[]> ToErrorDictionary(
        IEnumerable<ValidationResult> validationResults)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var result in validationResults)
        {
            var message = result.ErrorMessage ?? "Invalid value.";
            var members = result.MemberNames.Any() ? result.MemberNames : new[] { "" };
            foreach (var member in members)
            {
                errors[member] = errors.TryGetValue(member, out var existing)
                    ? existing.Append(message).ToArray()
                    : new[] { message };
            }
        }

        return errors;
    }
}
