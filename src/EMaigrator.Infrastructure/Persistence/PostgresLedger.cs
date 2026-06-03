using System.Runtime.CompilerServices;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed idempotency ledger. MarkAsync is an upsert keyed by the
/// UNIQUE(MailboxMigrationId, IdentityKey) index, so re-runs never create duplicate rows.
/// </summary>
public sealed class PostgresLedger : ILedger
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;

    public PostgresLedger(IDbContextFactory<EmaigratorDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<bool> IsDoneAsync(Guid mailboxMigrationId, string identityKey, CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var status = await ctx.LedgerEntries
            .Where(r => r.MailboxMigrationId == mailboxMigrationId && r.IdentityKey == identityKey)
            .Select(r => (LedgerStatus?)r.Status)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return status is LedgerStatus.Migrated or LedgerStatus.Skipped;
    }

    public async Task MarkAsync(Guid mailboxMigrationId, string identityKey, string sourceFolder,
        string destFolder, LedgerStatus status, string? errorCode, CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await ctx.LedgerEntries
            .FirstOrDefaultAsync(r => r.MailboxMigrationId == mailboxMigrationId && r.IdentityKey == identityKey, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            ctx.LedgerEntries.Add(new LedgerEntryRow
            {
                MailboxMigrationId = mailboxMigrationId,
                IdentityKey = identityKey,
                SourceFolder = sourceFolder,
                DestFolder = destFolder,
                Status = status,
                ErrorCode = errorCode,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.SourceFolder = sourceFolder;
            existing.DestFolder = destFolder;
            existing.Status = status;
            existing.ErrorCode = errorCode;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Concurrent insert lost the race to the unique index — re-apply as update.
            ctx.ChangeTracker.Clear();
            var row = await ctx.LedgerEntries
                .FirstAsync(r => r.MailboxMigrationId == mailboxMigrationId && r.IdentityKey == identityKey, ct)
                .ConfigureAwait(false);
            row.SourceFolder = sourceFolder;
            row.DestFolder = destFolder;
            row.Status = status;
            row.ErrorCode = errorCode;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<LedgerEntry> GetNotDoneAsync(
        Guid mailboxMigrationId, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = ctx.LedgerEntries.AsNoTracking()
            .Where(r => r.MailboxMigrationId == mailboxMigrationId
                        && (r.Status == LedgerStatus.Pending || r.Status == LedgerStatus.Failed))
            .OrderBy(r => r.Id)
            .AsAsyncEnumerable();

        await foreach (var r in query.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return new LedgerEntry(r.MailboxMigrationId, r.IdentityKey, r.SourceFolder,
                r.DestFolder, r.Status, r.ErrorCode, r.UpdatedAt);
        }
    }

    public async Task SeedPendingAsync(Guid mailboxMigrationId,
        IEnumerable<(string IdentityKey, string SourceFolder, string DestFolder)> messages,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var list = messages as ICollection<(string IdentityKey, string SourceFolder, string DestFolder)>
                   ?? messages.ToList();
        if (list.Count == 0)
        {
            return;
        }

        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Read the identity keys that already have a row for this migration. Existing rows (any
        // status — Pending/Migrated/Skipped/Failed) are left untouched: seeding NEVER downgrades a
        // done row back to Pending. Only genuinely-absent messages get a fresh Pending row.
        var keys = list.Select(m => m.IdentityKey).ToList();
        var existing = await ctx.LedgerEntries
            .Where(r => r.MailboxMigrationId == mailboxMigrationId && keys.Contains(r.IdentityKey))
            .Select(r => r.IdentityKey)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var seen = new HashSet<string>(existing, StringComparer.Ordinal);

        foreach (var (identityKey, sourceFolder, destFolder) in list)
        {
            // `seen` also guards against the same identityKey appearing twice within this batch.
            if (!seen.Add(identityKey))
            {
                continue;
            }

            ctx.LedgerEntries.Add(new LedgerEntryRow
            {
                MailboxMigrationId = mailboxMigrationId,
                IdentityKey = identityKey,
                SourceFolder = sourceFolder,
                DestFolder = destFolder,
                Status = LedgerStatus.Pending,
                ErrorCode = null,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        try
        {
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent seeder inserted one of these rows between our read and Save, hitting the
            // UNIQUE(MailboxMigrationId, IdentityKey) index. The losing rows already exist (possibly
            // already done) and must NOT be downgraded — so just drop our pending inserts.
            ctx.ChangeTracker.Clear();
        }
    }

    public async Task<LedgerCounts> GetCountsAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var grouped = await ctx.LedgerEntries.AsNoTracking()
            .Where(r => r.MailboxMigrationId == mailboxMigrationId)
            .GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.LongCount() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        long Get(LedgerStatus s) => grouped.Find(x => x.Key == s)?.Count ?? 0;
        return new LedgerCounts(Get(LedgerStatus.Migrated), Get(LedgerStatus.Skipped),
            Get(LedgerStatus.Failed), Get(LedgerStatus.Pending));
    }
}
