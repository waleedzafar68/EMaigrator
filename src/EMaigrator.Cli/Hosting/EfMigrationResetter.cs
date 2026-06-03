using EMaigrator.Cli.Commands;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Cli.Hosting;

/// <summary>Resets a MailboxMigration to Running (clearing FinishedAt) so a resume re-run can re-derive
/// a fresh terminal status. No-op if the row is missing.</summary>
public sealed class EfMigrationResetter(IDbContextFactory<EmaigratorDbContext> dbFactory) : IMigrationResetter
{
    public async Task ReopenAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var row = await ctx.MailboxMigrations.FirstOrDefaultAsync(m => m.Id == mailboxMigrationId, ct);
        if (row is null) return;
        row.Status = MailboxMigrationStatus.Running;
        row.FinishedAt = null;
        await ctx.SaveChangesAsync(ct);
    }
}
