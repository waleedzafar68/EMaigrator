using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.IntegrationTests.Persistence;

[Collection("postgres")]
public class PostgresLedgerTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public PostgresLedgerTests(PostgresFixture pg) => _pg = pg;

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

    [Fact]
    public async Task MarkAsync_is_idempotent_upsert()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();

        await ledger.MarkAsync(mig, "mid:<a@x>", "INBOX", "Inbox", LedgerStatus.Pending, null, default);
        await ledger.MarkAsync(mig, "mid:<a@x>", "INBOX", "Inbox", LedgerStatus.Migrated, null, default);

        await using var ctx = new EmaigratorDbContext(Options);
        var rows = await ctx.LedgerEntries.Where(r => r.MailboxMigrationId == mig).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Status.Should().Be(LedgerStatus.Migrated);
    }

    [Fact]
    public async Task IsDoneAsync_true_only_for_migrated_or_skipped()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();

        await ledger.MarkAsync(mig, "k1", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "k2", "f", "f", LedgerStatus.Skipped, null, default);
        await ledger.MarkAsync(mig, "k3", "f", "f", LedgerStatus.Failed, "E1", default);
        await ledger.MarkAsync(mig, "k4", "f", "f", LedgerStatus.Pending, null, default);

        (await ledger.IsDoneAsync(mig, "k1", default)).Should().BeTrue();
        (await ledger.IsDoneAsync(mig, "k2", default)).Should().BeTrue();
        (await ledger.IsDoneAsync(mig, "k3", default)).Should().BeFalse();
        (await ledger.IsDoneAsync(mig, "k4", default)).Should().BeFalse();
        (await ledger.IsDoneAsync(mig, "missing", default)).Should().BeFalse();
    }

    [Fact]
    public async Task GetNotDoneAsync_returns_pending_and_failed()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();
        await ledger.MarkAsync(mig, "done", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "pend", "f", "f", LedgerStatus.Pending, null, default);
        await ledger.MarkAsync(mig, "fail", "f", "f", LedgerStatus.Failed, "E", default);

        var notDone = new List<string>();
        await foreach (var e in ledger.GetNotDoneAsync(mig, default))
        {
            notDone.Add(e.IdentityKey);
        }

        notDone.Should().BeEquivalentTo(new[] { "pend", "fail" });
    }

    [Fact]
    public async Task GetCountsAsync_returns_per_status_counts()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();
        await ledger.MarkAsync(mig, "a", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "b", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "c", "f", "f", LedgerStatus.Skipped, null, default);
        await ledger.MarkAsync(mig, "d", "f", "f", LedgerStatus.Failed, "E", default);
        await ledger.MarkAsync(mig, "e", "f", "f", LedgerStatus.Pending, null, default);

        var counts = await ledger.GetCountsAsync(mig, default);
        counts.Should().Be(new LedgerCounts(Migrated: 2, Skipped: 1, Failed: 1, Pending: 1));
    }
}
