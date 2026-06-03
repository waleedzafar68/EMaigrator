using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Realtime;

/// <summary>
/// Resolves a <see cref="MailboxMigration"/> id to the owning Job id. The worker→bridge→SignalR flow
/// carries <c>MailboxMigrationId</c> on its events, but clients <c>Subscribe</c> to the SignalR group
/// keyed by the Job id (= <c>MigrationDto.id</c>); a Job fans out to N mailbox rows, so the two ids
/// differ. The bridge uses this to translate before pushing to the group clients actually joined.
/// </summary>
public interface IMailboxJobLookup
{
    Task<Guid?> GetJobIdAsync(Guid mailboxMigrationId, CancellationToken ct);
}

/// <summary>
/// DB-backed lookup. The bridge is a system/MassTransit consumer with NO tenant or HttpContext, so an
/// unfiltered factory-created context is correct here: the ambient query filter would fall back to
/// <see cref="Guid.Empty"/> (unfiltered) anyway, and the resolution is an internal id→id translation,
/// not a tenant-scoped read.
/// </summary>
public sealed class MailboxJobLookup : IMailboxJobLookup
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;

    public MailboxJobLookup(IDbContextFactory<EmaigratorDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<Guid?> GetJobIdAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Set<MailboxMigration>()
            .Where(m => m.Id == mailboxMigrationId)
            .Select(m => (Guid?)m.JobId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
