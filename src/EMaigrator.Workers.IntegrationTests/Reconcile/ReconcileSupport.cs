using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Persistence;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Core.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.Globalization;

namespace EMaigrator.Workers.IntegrationTests.Reconcile;

/// <summary>A source message the fake source yields; its content (bytes) is opened on demand.</summary>
public sealed record FakeSourceMessage(
    string MessageId, IReadOnlyList<CanonicalAttachmentInfo> Attachments, byte[] Content);

/// <summary>
/// In-memory source: yields a fixed list of messages from a single "Inbox" folder. When
/// IncludeAttachmentMetadata is requested it returns the configured attachment metadata; the body
/// bytes (which may carry a canary) are opened lazily and transit memory only.
/// </summary>
public sealed class FakeReconcileSource : ISourceProvider
{
    private readonly IReadOnlyList<FakeSourceMessage> _messages;

    public FakeReconcileSource(IReadOnlyList<FakeSourceMessage> messages) => _messages = messages;

    public ProviderId Id => new("imap");
    public ProviderConstraints Constraints => new();

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) =>
        Task.FromResult(new ConnectionTestResult(true, 1, _messages.Count));

    public Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CanonicalFolder>>(new[] { new CanonicalFolder(FolderPath.Parse("Inbox"), _messages.Count) });

    public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
        FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var m in _messages)
        {
            var bytes = m.Content;
            yield return new CanonicalMessage
            {
                IdentityKey = "mid:" + (IdentityKey.NormalizeMessageId(m.MessageId) ?? m.MessageId),
                MessageId = m.MessageId,
                InternalDate = DateTimeOffset.UnixEpoch,
                Attachments = options.IncludeAttachmentMetadata ? m.Attachments : [],
                OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
            };
            await Task.Yield();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Stateful in-memory destination implementing the reconcile capability. Models a live Exchange
/// folder: ScanFolderAsync reflects the CURRENT store, WriteMessageAsync adds a whole message, and
/// BackfillAttachmentsAsync adds the missing attachments onto an existing message. It opens and fully
/// reads every source stream (materialising any canary bytes) but persists NOTHING to disk/DB — it is
/// the destination, holding state only in RAM. Records every write/backfill so tests can assert
/// copy/backfill/skip classification, no-duplication, and idempotent re-runs.
/// </summary>
public sealed class StatefulReconcileDestination : IDestinationProvider, IReconcilableDestination
{
    private sealed class Record
    {
        public required string DestMessageId { get; init; }
        public required List<CanonicalAttachmentInfo> Attachments { get; init; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Record> _store = new(StringComparer.OrdinalIgnoreCase);

    public List<string> WrittenMessageIds { get; } = [];
    public List<(string DestMessageId, IReadOnlyList<string> Added)> Backfills { get; } = [];

    /// <summary>Pre-seed the destination as it exists BEFORE reconcile (messageId → its current attachments).</summary>
    public void Seed(string messageId, params CanonicalAttachmentInfo[] attachments)
    {
        var key = IdentityKey.NormalizeMessageId(messageId)!;
        lock (_gate)
        {
            _store[key] = new Record { DestMessageId = "dest-" + key, Attachments = attachments.ToList() };
        }
    }

    public ProviderId Id => new("graph");
    public ProviderConstraints Constraints => new();

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) =>
        Task.FromResult(new ConnectionTestResult(true, 1, 0));

    public Task EnsureFolderAsync(FolderPath folder, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
    {
        var key = IdentityKey.NormalizeMessageId(messageId);
        lock (_gate)
        {
            return Task.FromResult(key is not null && _store.ContainsKey(key));
        }
    }

    public async Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct)
    {
        // Materialise the body (canary bytes pass through memory) then discard — destination state is
        // metadata + attachment list only, held in RAM. Never written to disk/DB.
        await using (var s = await message.OpenContentAsync(ct).ConfigureAwait(false))
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms, ct).ConfigureAwait(false);
        }

        var key = IdentityKey.NormalizeMessageId(message.MessageId) ?? Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            WrittenMessageIds.Add(key);
            _store[key] = new Record { DestMessageId = "dest-" + key, Attachments = message.Attachments.ToList() };
        }

        return new WriteResult(true, "dest-" + key);
    }

    public async IAsyncEnumerable<DestMessageDigest> ScanFolderAsync(
        FolderPath folder, [EnumeratorCancellation] CancellationToken ct)
    {
        List<DestMessageDigest> snapshot;
        lock (_gate)
        {
            snapshot = _store
                .Select(kv => new DestMessageDigest("<" + kv.Key + ">", kv.Value.DestMessageId, kv.Value.Attachments.ToArray()))
                .ToList();
        }

        foreach (var d in snapshot)
        {
            yield return d;
            await Task.Yield();
        }
    }

    public async Task<BackfillResult> BackfillAttachmentsAsync(
        FolderPath folder, string destMessageId, CanonicalMessage source,
        IReadOnlyList<CanonicalAttachmentInfo> missing, CancellationToken ct)
    {
        // Open + fully read the source (canary attachment bytes pass through memory) then discard.
        await using (var s = await source.OpenContentAsync(ct).ConfigureAwait(false))
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms, ct).ConfigureAwait(false);
        }

        lock (_gate)
        {
            var rec = _store.Values.FirstOrDefault(r => r.DestMessageId == destMessageId);
            rec?.Attachments.AddRange(missing);
            Backfills.Add((destMessageId, missing.Select(m => m.FileName).ToArray()));
        }

        return new BackfillResult(missing.Count, 0);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // keep state across the consumer's create/dispose cycles
}

/// <summary>Returns the SAME fake source + dest singletons every time, so state persists across runs.</summary>
public sealed class FakeReconcileSessionFactory : IProviderSessionFactory
{
    private readonly ISourceProvider _source;
    private readonly IDestinationProvider _dest;

    public FakeReconcileSessionFactory(ISourceProvider source, IDestinationProvider dest)
    {
        _source = source;
        _dest = dest;
    }

    public Task<ISourceProvider> CreateSourceAsync(Guid mailboxMigrationId, CancellationToken ct) => Task.FromResult(_source);
    public Task<IDestinationProvider> CreateDestinationAsync(Guid mailboxMigrationId, CancellationToken ct) => Task.FromResult(_dest);
}

/// <summary>Builds a worker host that drives the REAL ReconcileConsumer over the live containers, with
/// only the provider boundary faked (sanctioned for the reconcile gates). Real Postgres ledger + EF
/// status writer + Redis rate-limiter + RabbitMQ bus.</summary>
public static class ReconcileHost
{
    public static IHost Build(
        EmaigratorPipelineFixture fx, IConfiguration config, Guid migrationId,
        MigrationConnections conns, ISourceProvider source, IDestinationProvider dest)
    {
        var orchestration = config.GetSection("Orchestration").Get<OrchestrationOptions>() ?? new OrchestrationOptions();

        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, b) => b.AddConfiguration(config))
            .ConfigureServices(services =>
            {
                services.AddInfrastructure(config, registerBus: false); // real ledger / rate-limiter / secrets / DbContext

                services.Configure<OrchestrationOptions>(config.GetSection("Orchestration"));
                services.AddSingleton<StreamingCopierFactory>();
                services.AddSingleton<IMigrationStatusWriter, EfMigrationStatusWriter>();
                services.AddSingleton<IRemediationPlanStore, EmptyRemediationStore>();
                services.AddSingleton<IMigrationConnectionLookup>(new TestConnectionLookup(migrationId, conns));
                services.AddSingleton<IProviderSessionFactory>(new FakeReconcileSessionFactory(source, dest));
                services.AddSingleton<IJobOrchestrator>(sp => new MassTransitJobOrchestrator(sp.GetRequiredService<IBus>()));

                services.AddMassTransit(x =>
                {
                    x.AddConsumer<ReconcileConsumer>();
                    x.UsingRabbitMq((ctx, cfg) =>
                    {
                        cfg.Host(new Uri(fx.RabbitMqConnectionString));
                        cfg.PrefetchCount = orchestration.ConsumerPrefetch;
                        cfg.UseMessageRetry(r => r.Immediate(orchestration.DlqRetryCount));
                        cfg.ConfigureEndpoints(ctx);
                    });
                });
            })
            .Build();
    }

    /// <summary>A graph destination descriptor carrying the accountEmail the consumer keys the rate-limiter on.</summary>
    public static MigrationConnections MakeConns(Guid jobId) => new(
        jobId, "tenant-1",
        new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
        new ConnectionDescriptor
        {
            Provider = new("graph"),
            Auth = AuthMethod.GraphAppOAuth,
            Settings = new Dictionary<string, string> { ["accountEmail"] = "dest@contoso.com" },
        });

    /// <summary>Persists the Job + a Pending MailboxMigration the EF status writer advances to terminal.</summary>
    public static async Task PersistJobAsync(IHost host, Guid jobId, Guid migrationId)
    {
        var factory = host.Services.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = Guid.NewGuid(),
            SourceProvider = new ProviderId("imap"),
            DestProvider = new ProviderId("graph"),
            Mode = JobMode.Reconcile,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        ctx.MailboxMigrations.Add(new MailboxMigration
        {
            Id = migrationId,
            JobId = jobId,
            SourceMailbox = "src@contoso.com",
            DestMailbox = "dest@contoso.com",
            Status = MailboxMigrationStatus.Pending,
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Polls the MailboxMigration row (bounded) until it reaches a terminal status.</summary>
    public static async Task<MailboxMigrationStatus> WaitTerminalAsync(IHost host, Guid migrationId, int maxSeconds = 120)
    {
        var factory = host.Services.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
        for (var attempt = 0; attempt < maxSeconds; attempt++)
        {
            await Task.Delay(1000);
            await using var ctx = await factory.CreateDbContextAsync();
            var row = await ctx.MailboxMigrations.AsNoTracking().FirstOrDefaultAsync(m => m.Id == migrationId);
            if (row is not null && row.Status is MailboxMigrationStatus.Completed
                or MailboxMigrationStatus.Partial or MailboxMigrationStatus.Failed)
            {
                return row.Status;
            }
        }

        return MailboxMigrationStatus.Running; // timed out (caller asserts terminal)
    }

    /// <summary>
    /// Scans EVERY text/varchar/char/json/jsonb/bytea column (cast to text) of EVERY public table for
    /// any of the sentinels; returns "table.column→count" for each match. Identifiers come from
    /// information_schema (not user input); the sentinel is parameterized.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ScanPostgresForAsync(string connectionString, params string[] sentinels)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var columns = new List<(string Table, string Column)>();
        await using (var colCmd = conn.CreateCommand())
        {
            colCmd.CommandText =
                """
                SELECT table_name, column_name FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND data_type IN ('text','character varying','character','json','jsonb','bytea')
                ORDER BY table_name, column_name
                """;
            await using var reader = await colCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var matches = new List<string>();
        foreach (var (table, column) in columns)
        {
            foreach (var sentinel in sentinels)
            {
                var sql = string.Create(CultureInfo.InvariantCulture,
                    $"SELECT COUNT(*) FROM public.\"{table}\" WHERE CAST(\"{column}\" AS text) LIKE @s");
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("s", "%" + sentinel + "%");
                var count = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    matches.Add($"{table}.{column}→{count}");
                }
            }
        }

        return matches;
    }
}
