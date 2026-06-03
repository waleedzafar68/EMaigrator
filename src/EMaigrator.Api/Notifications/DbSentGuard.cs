using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Notifications;

/// <summary>
/// Idempotency gate backed by <see cref="ApiSideContext"/>. Inserts a row keyed on the
/// mailbox-migration id; a unique-violation (two terminal events racing across instances) means
/// another consumer already claimed it, so this caller must NOT send.
/// </summary>
public sealed class DbSentGuard : ISentGuard
{
    private readonly ApiSideContext _side;

    public DbSentGuard(ApiSideContext side)
    {
        ArgumentNullException.ThrowIfNull(side);
        _side = side;
    }

    public async Task<bool> TryMarkSentAsync(Guid migrationId, CancellationToken ct)
    {
        if (await _side.NotificationsSent.AnyAsync(r => r.MailboxMigrationId == migrationId, ct).ConfigureAwait(false))
        {
            return false;
        }

        _side.NotificationsSent.Add(new NotificationSentRow
        {
            MailboxMigrationId = migrationId,
            SentAt = DateTimeOffset.UtcNow,
        });
        try
        {
            await _side.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException) // unique-violation: a concurrent consumer already inserted
        {
            return false;
        }
    }
}
