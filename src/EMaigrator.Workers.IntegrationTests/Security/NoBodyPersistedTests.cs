using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Security;

/// <summary>
/// Security gate (a): proves that ZERO message-body bytes are persisted to Postgres or disk by the
/// streaming pipeline. Seeds messages whose BODIES carry a unique sentinel, runs the real pipeline
/// to completion, then exhaustively scans (1) every NEW file under temp/working dirs and (2) every
/// text/json/bytea column of every public Postgres table for the sentinel. Any match is a real
/// finding — the assertions are not weakened.
/// </summary>
[Trait("Category", "Security")]
[Collection("pipeline")]
public sealed class NoBodyPersistedTests
{
    private const int MessageCount = 12;

    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;

    public NoBodyPersistedTests(EmaigratorPipelineFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private static TimeSpan Timeout => TimeSpan.FromMinutes(3);

    [Fact]
    public async Task No_message_body_bytes_reach_postgres_or_disk()
    {
        FaultInjectingMessageHydrator.PoisonEnabled = false;
        var sentinel = $"EMAIGRATOR_BODY_SENTINEL_{Guid.NewGuid():N}";

        // Snapshot disk BEFORE the run so we only flag files the pipeline created.
        var diskWatcher = TempDirWatcher.Snapshot();

        var seededIds = await _fx.SeedSourceWithBodySentinelAsync(MessageCount, sentinel);

        var migrationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var conns = new MigrationConnections(
            jobId,
            "tenant-sec",
            _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, null),
            _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, null));

        using var secretHost = BuildSecretHost();
        var srcSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        var dstSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        conns = conns with
        {
            Source = _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcSecret),
            Dest = _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstSecret),
        };

        await RunPipelineToCompletionAsync(migrationId, conns, seededIds);

        // ── Disk scan ──────────────────────────────────────────────────────────────────────────
        var newFiles = diskWatcher.NewFilesContaining(sentinel);

        // ── Postgres scan ──────────────────────────────────────────────────────────────────────
        var (matchedColumns, columnsScanned, tablesScanned) = await ScanPostgresForSentinelAsync(sentinel);

        // ── Evidence ───────────────────────────────────────────────────────────────────────────
        _out.WriteLine("=== NoBodyPersisted security evidence ===");
        _out.WriteLine($"sentinel              = {sentinel}");
        _out.WriteLine($"messages seeded       = {MessageCount} (sentinel in BODY only)");
        _out.WriteLine($"postgres tables scanned  = {tablesScanned}");
        _out.WriteLine($"postgres columns scanned = {columnsScanned} (text/varchar/char/json/jsonb/bytea, casts to text)");
        _out.WriteLine($"postgres columns with >0 matches = {matchedColumns.Count}");
        foreach (var (col, count) in matchedColumns)
        {
            _out.WriteLine($"  MATCH {col} -> {count} row(s)");
        }

        _out.WriteLine($"new disk files containing sentinel = {newFiles.Count}");
        foreach (var f in newFiles)
        {
            _out.WriteLine($"  DISK MATCH {f}");
        }

        _out.WriteLine(matchedColumns.Count == 0 && newFiles.Count == 0
            ? "RESULT: PASS — zero body bytes found in Postgres or on disk."
            : "RESULT: FAIL — body sentinel located (SECURITY FINDING).");

        // ── Assertions (not weakened) ────────────────────────────────────────────────────────────
        matchedColumns.Should().BeEmpty(
            "no Postgres column may contain the message-body sentinel (zero body persistence)");
        newFiles.Should().BeEmpty(
            "no file written during the run may contain the message-body sentinel (no body spill to disk)");
        columnsScanned.Should().BeGreaterThan(0, "the scan must actually inspect columns to be meaningful");
    }

    /// <summary>
    /// Scans EVERY text-bearing column (text/varchar/char/json/jsonb/bytea, cast to text) of EVERY
    /// public table for rows whose value contains the sentinel. Returns the per-column match counts
    /// (only columns with >0 are listed), the total columns scanned, and tables scanned.
    /// </summary>
    private async Task<(IReadOnlyList<(string Column, long Count)> Matches, int ColumnsScanned, int TablesScanned)>
        ScanPostgresForSentinelAsync(string sentinel)
    {
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();

        // Enumerate scannable columns.
        var columns = new List<(string Table, string Column, string DataType)>();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using (var colCmd = conn.CreateCommand())
        {
            colCmd.CommandText =
                """
                SELECT table_name, column_name, data_type
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND data_type IN ('text','character varying','character','json','jsonb','bytea')
                ORDER BY table_name, column_name
                """;
            await using var reader = await colCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                columns.Add((table, reader.GetString(1), reader.GetString(2)));
                tables.Add(table);
            }
        }

        var matches = new List<(string Column, long Count)>();
        var like = "%" + sentinel + "%";
        foreach (var (table, column, _) in columns)
        {
            // Cast EVERY candidate (incl. bytea/json) to text so a LIKE can see the raw bytes.
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

    private async Task RunPipelineToCompletionAsync(
        Guid migrationId, MigrationConnections conns, IReadOnlyCollection<string> expectedIds)
    {
        var config = _fx.BuildConfiguration();
        using var host = _fx.BuildHost(migrationId, conns, config);
        await host.StartAsync();
        try
        {
            var orchestrator = host.Services.GetRequiredService<IJobOrchestrator>();
            await orchestrator.EnqueueMigrationAsync(migrationId, CancellationToken.None);
            await WaitForDestinationAsync(expectedIds, Timeout);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private async Task WaitForDestinationAsync(IReadOnlyCollection<string> expectedIds, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var present = await _fx.MessageIdsAsync(EmaigratorPipelineFixture.DstEmail);
            if (expectedIds.All(present.Contains))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private IHost BuildSecretHost()
    {
        var config = _fx.BuildConfiguration();
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddInfrastructure(config, registerBus: false))
            .Build();
    }
}
