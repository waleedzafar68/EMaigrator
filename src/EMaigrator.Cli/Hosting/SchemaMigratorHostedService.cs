using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Cli.Hosting;

/// <summary>Applies EF migrations once at host start so the ledger schema exists before any command.
/// Database.MigrateAsync is idempotent (a no-op when current).</summary>
public sealed class SchemaMigratorHostedService(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync(cancellationToken);
        await ctx.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
