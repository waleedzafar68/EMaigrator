using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Workers.Persistence;

/// <summary>
/// EF-backed writer for the parent MailboxMigration lifecycle. SetRunning only advances a Pending
/// row; SetTerminal computes Completed/Partial from the ledger counts and is idempotent — it never
/// overwrites an already-terminal row (including Cancelled).
/// </summary>
public sealed class EfMigrationStatusWriter : IMigrationStatusWriter
{
    private static readonly MailboxMigrationStatus[] Terminal =
        [MailboxMigrationStatus.Completed, MailboxMigrationStatus.Partial,
         MailboxMigrationStatus.Failed, MailboxMigrationStatus.Cancelled];

    private readonly IDbContextFactory<EmaigratorDbContext> _factory;

    public EfMigrationStatusWriter(IDbContextFactory<EmaigratorDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task SetRunningAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx.MailboxMigrations
            .FirstOrDefaultAsync(m => m.Id == mailboxMigrationId, ct).ConfigureAwait(false);
        if (row is null || row.Status != MailboxMigrationStatus.Pending)
        {
            return; // missing, or already advanced past Pending → no-op
        }

        row.Status = MailboxMigrationStatus.Running;
        row.StartedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SetTerminalAsync(Guid mailboxMigrationId, LedgerCounts counts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(counts);
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx.MailboxMigrations
            .FirstOrDefaultAsync(m => m.Id == mailboxMigrationId, ct).ConfigureAwait(false);
        if (row is null || Array.IndexOf(Terminal, row.Status) >= 0)
        {
            return; // already terminal (incl. Cancelled) → idempotent no-op
        }

        row.Status = counts.Failed == 0 ? MailboxMigrationStatus.Completed : MailboxMigrationStatus.Partial;
        row.MigratedCount = counts.Migrated;
        row.SkippedCount = counts.Skipped;
        row.FailedCount = counts.Failed;
        row.FinishedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SetNotSupportedAsync(Guid mailboxMigrationId, string reason, CancellationToken ct)
    {
        // The reason is the why (logged by the consumer); the persisted signal is a terminal Failed
        // row (no schema column for a free-text reason — keeps the no-body-persistence surface unchanged).
        _ = reason;
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await ctx.MailboxMigrations
            .FirstOrDefaultAsync(m => m.Id == mailboxMigrationId, ct).ConfigureAwait(false);
        if (row is null || Array.IndexOf(Terminal, row.Status) >= 0)
        {
            return; // already terminal → idempotent no-op
        }

        row.Status = MailboxMigrationStatus.Failed;
        row.FinishedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
