using EMaigrator.Cli.Commands;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Cli.Hosting;

/// <summary>Live IMigrationStateReader: reads MailboxMigration.Status and returns its enum name
/// (Pending while in flight; Completed/Partial/Failed/Cancelled terminal — matching RunCommand.TerminalStatuses).</summary>
public sealed class EfMigrationStateReader(IDbContextFactory<EmaigratorDbContext> dbFactory)
    : IMigrationStateReader
{
    public async Task<string> GetStatusAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var row = await ctx.MailboxMigrations.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mailboxMigrationId, ct);
        return row is null ? MailboxMigrationStatus.Pending.ToString() : row.Status.ToString();
    }
}
