using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EMaigrator.Infrastructure.IntegrationTests.Data;

[Collection("postgres")]
public class MigrationApplyTests
{
    private readonly PostgresFixture _pg;

    public MigrationApplyTests(PostgresFixture pg) => _pg = pg;

    private EmaigratorDbContext NewContext() =>
        new(new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options);

    [Fact]
    public async Task Migration_creates_all_tables()
    {
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", conn);
        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        tables.Should().Contain(
        [
            "jobs", "mailbox_migrations", "folder_tasks",
            "ledger_entries", "migration_logs", "credentials", "tenants"
        ]);
    }

    [Fact]
    public async Task Migration_creates_ledger_unique_index()
    {
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'ledger_entries'", conn);
        var defs = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            defs.Add(reader.GetString(0));
        }

        defs.Should().Contain(d =>
            d.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
            d.Contains("MailboxMigrationId", StringComparison.OrdinalIgnoreCase) &&
            d.Contains("IdentityKey", StringComparison.OrdinalIgnoreCase));
    }
}
