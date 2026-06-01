using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Retention;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.IntegrationTests.Retention;

[Collection("postgres")]
public class LogRetentionPurgeTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public LogRetentionPurgeTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> options)
        : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(options);
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Purges_only_logs_older_than_retention()
    {
        var now = DateTimeOffset.UtcNow;
        var mig = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.MigrationLogs.Add(new MigrationLogRow
            {
                MailboxMigrationId = mig,
                SourceFolder = "f",
                DestFolder = "f",
                Status = "Migrated",
                CreatedAt = now.AddDays(-31),
            });
            ctx.MigrationLogs.Add(new MigrationLogRow
            {
                MailboxMigrationId = mig,
                SourceFolder = "f",
                DestFolder = "f",
                Status = "Migrated",
                CreatedAt = now.AddDays(-29),
            });
            await ctx.SaveChangesAsync();
        }

        var svc = new LogRetentionPurgeService(
            new Factory(DbOptions),
            Options.Create(new RetentionOptions { LogRetentionDays = 30 }),
            NullLogger<LogRetentionPurgeService>.Instance);

        var deleted = await svc.PurgeOnceAsync(now, default);

        deleted.Should().Be(1);
        await using var verify = new EmaigratorDbContext(DbOptions);
        (await verify.MigrationLogs.CountAsync(r => r.MailboxMigrationId == mig)).Should().Be(1);
    }
}
