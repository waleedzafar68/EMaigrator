using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Workers.Persistence;

/// <summary>
/// Rolls a job's <see cref="Job.Status"/> up to a terminal <see cref="JobStatus"/> once every one of its
/// <see cref="MailboxMigration"/> rows is terminal. Mode-agnostic — drives completion for both migrate and
/// reconcile.
/// </summary>
public interface IJobStatusFinalizer
{
    /// <summary>
    /// If every mailbox of the job owning <paramref name="mailboxMigrationId"/> is terminal, set the job's
    /// rolled-up terminal status (<see cref="JobStatus.Partial"/> if any mailbox failed/partial, else
    /// <see cref="JobStatus.Completed"/>) and return it. Returns <c>null</c> when the job is not yet done OR
    /// is already terminal (idempotent — a Cancelled/already-finalized job is never overwritten, and a null
    /// return breaks the terminal-event re-publish cycle).
    /// </summary>
    Task<JobStatus?> FinalizeIfDoneAsync(Guid mailboxMigrationId, CancellationToken ct);
}

/// <inheritdoc />
public sealed class EfJobStatusFinalizer : IJobStatusFinalizer
{
    private static readonly MailboxMigrationStatus[] MailboxTerminal =
        [MailboxMigrationStatus.Completed, MailboxMigrationStatus.Partial,
         MailboxMigrationStatus.Failed, MailboxMigrationStatus.Cancelled];

    private static readonly JobStatus[] JobTerminal =
        [JobStatus.Completed, JobStatus.Partial, JobStatus.Failed, JobStatus.Cancelled];

    private readonly IDbContextFactory<EmaigratorDbContext> _factory;

    public EfJobStatusFinalizer(IDbContextFactory<EmaigratorDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<JobStatus?> FinalizeIfDoneAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        // Factory context is unfiltered (CurrentTenantId == Guid.Empty), so this rolls up across tenants.
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var mailbox = await ctx.MailboxMigrations.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mailboxMigrationId, ct).ConfigureAwait(false);
        if (mailbox is null)
        {
            return null;
        }

        var job = await ctx.Jobs.FirstOrDefaultAsync(j => j.Id == mailbox.JobId, ct).ConfigureAwait(false);
        if (job is null || Array.IndexOf(JobTerminal, job.Status) >= 0)
        {
            // Unknown job, or already terminal (incl. Cancelled) → idempotent no-op. Returning null here is
            // what stops the terminal MigrationProgressEvent from re-triggering an endless publish loop.
            return null;
        }

        var mailboxes = await ctx.MailboxMigrations.AsNoTracking()
            .Where(m => m.JobId == job.Id)
            .Select(m => m.Status)
            .ToListAsync(ct).ConfigureAwait(false);

        // Gate on ALL mailboxes terminal — NOT a single ledger's Pending==0 — so a resume that re-seeds
        // Pending on one mailbox can never prematurely finalize the job (docs/KNOWN-ISSUES.md race).
        if (mailboxes.Count == 0 || mailboxes.Any(s => Array.IndexOf(MailboxTerminal, s) < 0))
        {
            return null;
        }

        var anyFailedOrPartial = mailboxes.Any(s =>
            s == MailboxMigrationStatus.Failed || s == MailboxMigrationStatus.Partial);
        job.Status = anyFailedOrPartial ? JobStatus.Partial : JobStatus.Completed;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return job.Status;
    }
}
