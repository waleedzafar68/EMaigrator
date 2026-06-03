using System.Globalization;
using System.Text.Json;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Security;

/// <summary>
/// Security gate (USER-GATE): proves ZERO message-body bytes are persisted to Postgres along the
/// REAL production-seam path (real hydrator → StreamingMessageCopier). Seeds messages whose BODIES
/// carry a unique sentinel, runs the migration to terminal Completed via the production seams
/// (BuildHostWithRealSeams + a persisted Job/MailboxMigration — no per-message test doubles), then
/// exhaustively scans every text/varchar/char/json/jsonb/bytea column (cast to text) of every public
/// Postgres table for the sentinel. Any match is a real finding; the assertion is not weakened.
/// </summary>
[Trait("Category", "Security")]
[Collection("pipeline")]
public sealed class RealSeamNoBodyPersistenceTests
{
    private const int MessageCount = 5;

    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;

    public RealSeamNoBodyPersistenceTests(EmaigratorPipelineFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Fact]
    public async Task No_body_bytes_reach_postgres_via_real_seams()
    {
        var sentinel = $"EMAIGRATOR_BODY_SENTINEL_{Guid.NewGuid():N}";
        var seededIds = await _fx.SeedSourceWithBodySentinelAsync(MessageCount, sentinel);
        seededIds.Should().HaveCount(MessageCount);

        var config = _fx.BuildConfiguration(batchSize: 2);
        using var host = _fx.BuildHostWithRealSeams(config);
        await host.StartAsync();
        try
        {
            var srcRef = await EmaigratorPipelineFixture.StorePasswordSecretAsync(host, "self-host");
            var dstRef = await EmaigratorPipelineFixture.StorePasswordSecretAsync(host, "self-host");
            var srcDesc = _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcRef);
            var dstDesc = _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstRef);

            var jobId = Guid.NewGuid();
            var migrationId = Guid.NewGuid();
            var factory = host.Services.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Jobs.Add(new Job
                {
                    Id = jobId,
                    TenantId = Guid.NewGuid(),
                    SourceProvider = new ProviderId("imap"),
                    DestProvider = new ProviderId("imap"),
                    SourceConnectionRef = JsonSerializer.Serialize(srcDesc),
                    DestConnectionRef = JsonSerializer.Serialize(dstDesc),
                });
                ctx.MailboxMigrations.Add(new MailboxMigration
                {
                    Id = migrationId,
                    JobId = jobId,
                    SourceMailbox = EmaigratorPipelineFixture.SrcEmail,
                    DestMailbox = EmaigratorPipelineFixture.DstEmail,
                    Status = MailboxMigrationStatus.Pending,
                });
                await ctx.SaveChangesAsync();
            }

            await host.Services.GetRequiredService<IJobOrchestrator>()
                .EnqueueMigrationAsync(migrationId, CancellationToken.None);

            // Wait (bounded ~3 min) for terminal status — proves the bodies fully transited the copier.
            MailboxMigration row = null!;
            for (var attempt = 0; attempt < 180; attempt++)
            {
                await Task.Delay(1000);
                await using var ctx = await factory.CreateDbContextAsync();
                row = await ctx.MailboxMigrations.AsNoTracking().FirstAsync(m => m.Id == migrationId);
                if (row.Status is MailboxMigrationStatus.Completed
                    or MailboxMigrationStatus.Partial or MailboxMigrationStatus.Failed)
                {
                    break;
                }
            }

            row.Status.Should().Be(MailboxMigrationStatus.Completed);
            (await _fx.CountAllAsync(EmaigratorPipelineFixture.DstEmail)).Should().Be(MessageCount,
                "the bodies DID transit (every message was copied) — so the scan below tests a real run");

            var (matchedColumns, columnsScanned, tablesScanned) = await ScanPostgresForSentinelAsync(sentinel);

            _out.WriteLine("=== RealSeam NoBodyPersistence security evidence ===");
            _out.WriteLine($"sentinel                 = {sentinel}");
            _out.WriteLine($"messages migrated        = {MessageCount} (sentinel in BODY only; REAL production seams)");
            _out.WriteLine($"MailboxMigration.Status  = {row.Status} (MigratedCount={row.MigratedCount})");
            _out.WriteLine($"postgres tables scanned  = {tablesScanned}");
            _out.WriteLine($"postgres columns scanned = {columnsScanned} (text/varchar/char/json/jsonb/bytea, cast to text)");
            _out.WriteLine($"columns with >0 matches  = {matchedColumns.Count}");
            foreach (var (col, count) in matchedColumns)
            {
                _out.WriteLine($"  MATCH {col} -> {count} row(s)");
            }

            _out.WriteLine(matchedColumns.Count == 0
                ? "RESULT: PASS — zero body bytes found in Postgres along the real-seam path."
                : "RESULT: FAIL — body sentinel located (SECURITY FINDING).");

            matchedColumns.Should().BeEmpty(
                "no Postgres column may contain the message-body sentinel (zero body persistence — DESIGN rule #6)");
            columnsScanned.Should().BeGreaterThan(0, "the scan must actually inspect columns to be meaningful");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Scans EVERY text-bearing column (text/varchar/char/json/jsonb/bytea, cast to text) of EVERY
    /// public table for rows whose value contains the sentinel. Table/column names come from
    /// information_schema (not user input), so the dynamic SQL is safe; the sentinel is parameterized.
    /// </summary>
    private async Task<(IReadOnlyList<(string Column, long Count)> Matches, int ColumnsScanned, int TablesScanned)>
        ScanPostgresForSentinelAsync(string sentinel)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();

        var columns = new List<(string Table, string Column)>();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using (var colCmd = conn.CreateCommand())
        {
            colCmd.CommandText =
                """
                SELECT table_name, column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND data_type IN ('text','character varying','character','json','jsonb','bytea')
                ORDER BY table_name, column_name
                """;
            await using var reader = await colCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                columns.Add((table, reader.GetString(1)));
                tables.Add(table);
            }
        }

        var matches = new List<(string Column, long Count)>();
        var like = "%" + sentinel + "%";
        foreach (var (table, column) in columns)
        {
            var sql = string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT COUNT(*) FROM public.\"{table}\" WHERE CAST(\"{column}\" AS text) LIKE @s");
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("s", like);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
            if (count > 0)
            {
                matches.Add(($"{table}.{column}", count));
            }
        }

        return (matches, columns.Count, tables.Count);
    }
}
