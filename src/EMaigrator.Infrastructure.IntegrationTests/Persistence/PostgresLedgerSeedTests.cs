using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.IntegrationTests.Persistence;

[Collection("postgres")]
public class PostgresLedgerSeedTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public PostgresLedgerSeedTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> Options =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private IDbContextFactory<EmaigratorDbContext> Factory => new TestContextFactory(Options);

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(Options);
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestContextFactory(DbContextOptions<EmaigratorDbContext> options)
        : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(options);
    }

    private static (string IdentityKey, string SourceFolder, string DestFolder) Msg(int i)
        => ($"mid-{i}", "INBOX", "INBOX");

    [Fact]
    public async Task Seeds_pending_rows_and_counts_them()
    {
        var ledger = new PostgresLedger(Factory);
        var mid = Guid.NewGuid();

        await ledger.SeedPendingAsync(mid, new[] { Msg(0), Msg(1), Msg(2) }, CancellationToken.None);

        var counts = await ledger.GetCountsAsync(mid, CancellationToken.None);
        counts.Pending.Should().Be(3);
        counts.Migrated.Should().Be(0);
    }

    [Fact]
    public async Task Reseeding_is_idempotent_and_never_downgrades_a_done_row()
    {
        var ledger = new PostgresLedger(Factory);
        var mid = Guid.NewGuid();

        await ledger.SeedPendingAsync(mid, new[] { Msg(0), Msg(1) }, CancellationToken.None);
        await ledger.MarkAsync(mid, "mid-0", "INBOX", "INBOX", LedgerStatus.Migrated, null, CancellationToken.None);

        // Re-seed the SAME set (as a resume would): the Migrated row must stay Migrated.
        await ledger.SeedPendingAsync(mid, new[] { Msg(0), Msg(1) }, CancellationToken.None);

        var counts = await ledger.GetCountsAsync(mid, CancellationToken.None);
        counts.Migrated.Should().Be(1);
        counts.Pending.Should().Be(1);
    }

    [Fact]
    public async Task Seeded_rows_are_returned_by_GetNotDoneAsync()
    {
        var ledger = new PostgresLedger(Factory);
        var mid = Guid.NewGuid();

        await ledger.SeedPendingAsync(mid, new[] { Msg(0) }, CancellationToken.None);

        var notDone = new List<LedgerEntry>();
        await foreach (var e in ledger.GetNotDoneAsync(mid, CancellationToken.None))
        {
            notDone.Add(e);
        }

        notDone.Should().ContainSingle().Which.IdentityKey.Should().Be("mid-0");
    }
}
