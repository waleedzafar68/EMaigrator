using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Api.Services;
using EMaigrator.Core.Preflight;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The wizard's scope step (Task 5): <c>PUT /migrations/{id}/scope</c> accepts either a JSON
/// <see cref="ScopeRequest"/> (one or more explicit pairs) or a <c>multipart/form-data</c> CSV upload
/// (columns <c>source_mailbox,destination_mailbox</c>). It parses + validates the input into
/// <see cref="MailboxMigration"/> rows, replaces the job's existing mailbox rows, sets
/// <see cref="Job.IsBatch"/>, advances the wizard to step 3, and returns the refreshed
/// <see cref="MigrationDto"/>. The route runs through the tenant-filtered <see cref="EmaigratorDbContext"/>,
/// so a cross-tenant id is invisible (404); the fallback policy rejects anonymous callers (401).
/// </summary>
public static class ScopeEndpoints
{
    private static readonly string[] PairsRequired = ["at least one mailbox pair is required"];

    public static RouteGroupBuilder MapScopeEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The handler reads request.Form directly for the multipart CSV path; the upload arrives from the
        // SPA with a bearer token, so antiforgery does not apply (CSRF is mitigated by SameSite cookie +
        // bearer). DisableAntiforgery keeps the minimal-API form binding from demanding a token.
        group.MapPut("/migrations/{id:guid}/scope", ScopeAsync).DisableAntiforgery();

        return group;
    }

    private static async Task<IResult> ScopeAsync(Guid id, HttpRequest request, EmaigratorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);

        // The tenant query filter confines this lookup to the caller's tenant; a cross-tenant id is null.
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<MailboxPair> pairs;
        bool isBatch;

        if (request.HasFormContentType && request.Form.Files.Count > 0)
        {
            try
            {
                await using var stream = request.Form.Files[0].OpenReadStream();
                pairs = CsvMailboxParser.Parse(stream);
            }
            catch (CsvValidationException ex)
            {
                return Results.BadRequest(new { errors = new[] { ex.Message } });
            }

            isBatch = true;
        }
        else
        {
            var scope = await request.ReadFromJsonAsync<ScopeRequest>();
            if (scope?.Pairs is null || scope.Pairs.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["pairs"] = PairsRequired });
            }

            pairs = scope.Pairs.Select(p => new MailboxPair(p.SourceMailbox, p.DestMailbox)).ToList();
            isBatch = scope.IsBatch;
        }

        // Replace the job's existing mailbox rows with the new scope.
        var existing = await db.MailboxMigrations.Where(m => m.JobId == id).ToListAsync();
        db.MailboxMigrations.RemoveRange(existing);
        foreach (var p in pairs)
        {
            db.MailboxMigrations.Add(new MailboxMigration
            {
                Id = Guid.NewGuid(),
                JobId = id,
                SourceMailbox = p.SourceMailbox,
                DestMailbox = p.DestMailbox,
                Status = MailboxMigrationStatus.Pending,
            });
        }

        job.IsBatch = isBatch;
        job.WizardStep = Math.Max(job.WizardStep, 3);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var mailboxes = await db.MailboxMigrations.AsNoTracking().Where(m => m.JobId == id).ToListAsync();
        return Results.Ok(MigrationMapper.ToDto(job, mailboxes));
    }
}
