using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Identity;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Notifications;

/// <summary>
/// Joins MailboxMigration -> Job -> the owning tenant's first user; maps provider ids to display labels.
/// Runs in a background scope (no HTTP principal), so it bypasses the tenant query filter via
/// <c>IgnoreQueryFilters()</c> and loads by key.
/// </summary>
public sealed class DbNotificationRecipientResolver : INotificationRecipientResolver
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["imap"] = "WorkMail",
        ["graph"] = "Microsoft 365",
        ["gmail"] = "Google",
    };

    private readonly EmaigratorDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public DbNotificationRecipientResolver(EmaigratorDbContext db, UserManager<ApplicationUser> users)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(users);
        (_db, _users) = (db, users);
    }

    public async Task<NotificationContext?> ResolveAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        var row = await _db.Set<MailboxMigration>().IgnoreQueryFilters()
            .Where(m => m.Id == mailboxMigrationId)
            .Join(
                _db.Set<Job>().IgnoreQueryFilters(),
                m => m.JobId,
                j => j.Id,
                (m, j) => new { j.TenantId, j.SourceProvider, j.DestProvider })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var user = await _users.Users.Where(u => u.TenantId == row.TenantId)
            .OrderBy(u => u.Email).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (user?.Email is null)
        {
            return null;
        }

        var from = Labels.GetValueOrDefault(row.SourceProvider.Value, row.SourceProvider.Value);
        var to = Labels.GetValueOrDefault(row.DestProvider.Value, row.DestProvider.Value);
        return new NotificationContext(user.Email, from, to);
    }
}
