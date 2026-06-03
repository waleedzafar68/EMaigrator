using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Workers.IntegrationTests;

[Collection("pipeline")]
public class EfMigrationStatusWriterTests
{
    private readonly EmaigratorPipelineFixture _fx;
    public EfMigrationStatusWriterTests(EmaigratorPipelineFixture fx) => _fx = fx;

    private IDbContextFactory<EmaigratorDbContext> Factory()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_fx.BuildConfiguration(), registerBus: false);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
    }

    private static async Task<Guid> SeedMigrationAsync(IDbContextFactory<EmaigratorDbContext> f, MailboxMigrationStatus status)
    {
        var id = Guid.NewGuid();
        await using var ctx = await f.CreateDbContextAsync();
        ctx.MailboxMigrations.Add(new MailboxMigration
        {
            Id = id,
            JobId = Guid.NewGuid(),
            SourceMailbox = "s",
            DestMailbox = "d",
            Status = status,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private static async Task<MailboxMigration> ReadAsync(IDbContextFactory<EmaigratorDbContext> f, Guid id)
    {
        await using var ctx = await f.CreateDbContextAsync();
        return await ctx.MailboxMigrations.AsNoTracking().FirstAsync(m => m.Id == id);
    }

    [Fact]
    public async Task SetRunning_then_SetTerminal_completed()
    {
        var f = Factory();
        var id = await SeedMigrationAsync(f, MailboxMigrationStatus.Pending);
        var writer = new EfMigrationStatusWriter(f);

        await writer.SetRunningAsync(id, CancellationToken.None);
        (await ReadAsync(f, id)).Status.Should().Be(MailboxMigrationStatus.Running);

        await writer.SetTerminalAsync(id, new LedgerCounts(3, 0, 0, 0), CancellationToken.None);
        var done = await ReadAsync(f, id);
        done.Status.Should().Be(MailboxMigrationStatus.Completed);
        done.MigratedCount.Should().Be(3);
        done.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetTerminal_with_failures_is_partial_and_is_idempotent()
    {
        var f = Factory();
        var id = await SeedMigrationAsync(f, MailboxMigrationStatus.Running);
        var writer = new EfMigrationStatusWriter(f);

        await writer.SetTerminalAsync(id, new LedgerCounts(8, 0, 2, 0), CancellationToken.None);
        (await ReadAsync(f, id)).Status.Should().Be(MailboxMigrationStatus.Partial);

        // Duplicate progress event → second call must NOT change the terminal row.
        await writer.SetTerminalAsync(id, new LedgerCounts(10, 0, 0, 0), CancellationToken.None);
        var row = await ReadAsync(f, id);
        row.Status.Should().Be(MailboxMigrationStatus.Partial);
        row.FailedCount.Should().Be(2);
    }

    [Fact]
    public async Task SetTerminal_leaves_cancelled_alone()
    {
        var f = Factory();
        var id = await SeedMigrationAsync(f, MailboxMigrationStatus.Cancelled);
        var writer = new EfMigrationStatusWriter(f);

        await writer.SetTerminalAsync(id, new LedgerCounts(1, 0, 0, 0), CancellationToken.None);

        (await ReadAsync(f, id)).Status.Should().Be(MailboxMigrationStatus.Cancelled);
    }
}
