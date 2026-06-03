using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.RateLimiting;
using EMaigrator.Workers;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Persistence;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace EMaigrator.Workers.IntegrationTests;

/// <summary>
/// Boots the full real-infra trio (Postgres + Redis + RabbitMQ) plus a GreenMail IMAP server
/// holding TWO mailboxes (src@ / dst@) and exposes everything the end-to-end pipeline tests need:
/// schema migration, mailbox seeding/counting, ledger queries, and a worker host built against
/// these live containers. Started once per test collection.
/// </summary>
public sealed class EmaigratorPipelineFixture : IAsyncLifetime
{
    public const string SrcEmail = "src@local.test";
    public const string DstEmail = "dst@local.test";
    public const string MailPassword = "pw";
    private const int GreenMailApiPort = 8080;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("emaigrator").WithUsername("emaigrator").WithPassword("emaigrator").Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly RabbitMqContainer _rabbit =
        new RabbitMqBuilder("rabbitmq:3.13-management-alpine").Build();

    private IContainer _greenMail = null!;

    public string Host { get; private set; } = "127.0.0.1";
    public int ImapPort { get; private set; }
    public int SmtpPort { get; private set; }
    public int ApiPort { get; private set; }

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();
    public string RabbitMqConnectionString => _rabbit.GetConnectionString();

    private string _masterKey = "";

    public async Task InitializeAsync()
    {
        _greenMail = new ContainerBuilder("greenmail/standalone:2.1.0")
            .WithEnvironment("GREENMAIL_OPTS",
                "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.verbose")
            .WithPortBinding(3143, true)
            .WithPortBinding(3025, true)
            .WithPortBinding(GreenMailApiPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged(new Regex("Starting GreenMail API server")))
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _rabbit.StartAsync(),
            _greenMail.StartAsync());

        ImapPort = _greenMail.GetMappedPublicPort(3143);
        SmtpPort = _greenMail.GetMappedPublicPort(3025);
        ApiPort = _greenMail.GetMappedPublicPort(GreenMailApiPort);

        await SeedUserAsync(SrcEmail);
        await SeedUserAsync(DstEmail);

        _masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Migrate the schema once against the live Postgres using a throwaway DI graph.
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(), registerBus: false);
        await using var sp = services.BuildServiceProvider(validateScopes: true);
        await using var scope = sp.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
        await using var ctx = factory.CreateDbContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_redisMux is not null)
        {
            await _redisMux.DisposeAsync();
        }

        await _greenMail.DisposeAsync();
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask());
    }

    // ── Configuration ───────────────────────────────────────────────────────────────────────

    public IConfiguration BuildConfiguration(int batchSize = 1, int prefetch = 8, int dlqRetryCount = 2) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Infrastructure:PostgresConnectionString"] = PostgresConnectionString,
            ["Infrastructure:RedisConnectionString"] = RedisConnectionString,
            ["Infrastructure:RabbitMqConnectionString"] = RabbitMqConnectionString,
            ["Infrastructure:SecretStore:Mode"] = "LocalKey",
            ["Infrastructure:SecretStore:KeyRef"] = _masterKey,
            // Generous bucket so 20 sequential copies never throttle (throttling would fault the batch).
            ["Infrastructure:RateLimit:Buckets:imap:RefillPerSecond"] = "1000",
            ["Infrastructure:RateLimit:Buckets:imap:Burst"] = "1000",
            ["Infrastructure:RateLimit:Buckets:default:RefillPerSecond"] = "1000",
            ["Infrastructure:RateLimit:Buckets:default:Burst"] = "1000",
            // Worker / orchestration knobs.
            ["Workers:UseInMemoryTransport"] = "false",
            ["ConnectionStrings:RabbitMq"] = RabbitMqConnectionString,
            ["Orchestration:BatchSize"] = batchSize.ToString(CultureInfo.InvariantCulture),
            ["Orchestration:ConsumerPrefetch"] = prefetch.ToString(CultureInfo.InvariantCulture),
            ["Orchestration:DlqRetryCount"] = dlqRetryCount.ToString(CultureInfo.InvariantCulture),
        }).Build();

    // ── Host construction ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a worker host wired to the live containers. Mirrors <see cref="WorkerServiceRegistration"/>'s
    /// non-bus services but composes its OWN MassTransit registration so it can additionally host the
    /// <see cref="CollectingNeedsDecisionConsumer"/> that captures DLQ decisions.
    /// </summary>
    public IHost BuildHost(Guid migrationId, MigrationConnections conns, IConfiguration config)
    {
        var orchestration = config.GetSection("Orchestration").Get<OrchestrationOptions>() ?? new OrchestrationOptions();

        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, b) => b.AddConfiguration(config))
            .ConfigureServices(services =>
            {
                services.AddInfrastructure(config, registerBus: false);
                services.AddImapConnector();

                // Worker non-bus services (copied from WorkerServiceRegistration).
                services.Configure<OrchestrationOptions>(config.GetSection("Orchestration"));
                services.AddSingleton<IMigrationControlGate, RedisMigrationControlGate>();
                services.AddSingleton<IProviderSessionFactory, ProviderSessionFactory>();
                services.AddSingleton<StreamingCopierFactory>();
                services.AddSingleton<IJobOrchestrator>(sp =>
                    new MassTransitJobOrchestrator(sp.GetRequiredService<IBus>()));
                services.AddHostedService<CrashResumeStartupService>();

                // Test seams.
                services.AddSingleton<IMigrationConnectionLookup>(new TestConnectionLookup(migrationId, conns));
                services.AddSingleton<IMessageRefLister, ImapMessageRefLister>();
                services.AddSingleton<IMessageHydrator, FaultInjectingMessageHydrator>();
                services.AddSingleton<IRemediationPlanStore, EmptyRemediationStore>();
                services.AddSingleton<IJobMigrationLookup, EmptyJobMigrationLookup>();
                services.AddSingleton<IInterruptedJobLookup, TestInterruptedJobLookup>();

                // Real EF status writer: StartMigrationConsumer now depends on it (08R). It loads the
                // MailboxMigration by id; these test-double E2E runs don't persist one, so it no-ops.
                services.AddSingleton<IMigrationStatusWriter, EfMigrationStatusWriter>();

                services.AddMassTransit(x =>
                {
                    x.AddConsumer<StartMigrationConsumer>();
                    x.AddConsumer<MigrateFolderConsumer>();
                    x.AddConsumer<MigrateBatchConsumer>();
                    x.AddConsumer<MigrateBatchFaultConsumer>();
                    x.AddConsumer<JobControlConsumer>();
                    x.AddConsumer<CollectingNeedsDecisionConsumer>();

                    x.UsingRabbitMq((ctx, cfg) =>
                    {
                        cfg.Host(new Uri(RabbitMqConnectionString));
                        cfg.PrefetchCount = orchestration.ConsumerPrefetch;
                        cfg.UseMessageRetry(r => r.Immediate(orchestration.DlqRetryCount));
                        cfg.ConfigureEndpoints(ctx);
                    });
                });
            })
            .Build();
    }

    // ── Secret store helper ─────────────────────────────────────────────────────────────────

    /// <summary>Stores {"password":"pw"} for a tenant and returns its secretRef (used as the descriptor SecretRef).</summary>
    public static async Task<string> StorePasswordSecretAsync(IHost host, string tenantId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        return await secrets.StoreAsync(tenantId, $"{{\"password\":\"{MailPassword}\"}}", CancellationToken.None);
    }

    public ConnectionDescriptor Descriptor(string email, string? secretRef) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        SecretRef = secretRef,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = Host,
            ["port"] = ImapPort.ToString(CultureInfo.InvariantCulture),
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = email,
        },
    };

    // ── Ledger ──────────────────────────────────────────────────────────────────────────────

    public async Task<LedgerCounts> GetLedgerCountsAsync(Guid migrationId)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfiguration(), registerBus: false);
        await using var sp = services.BuildServiceProvider(validateScopes: true);
        await using var scope = sp.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
        return await ledger.GetCountsAsync(migrationId, CancellationToken.None);
    }

    // ── GreenMail seeding / counting ────────────────────────────────────────────────────────

    public async Task SeedUserAsync(string email)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{Host}:{ApiPort}") };
        var payload = $$"""{"email":"{{email}}","login":"{{email}}","password":"{{MailPassword}}"}""";
        HttpRequestException? last = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(new Uri("/api/user", UriKind.Relative), content);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException(
            $"GreenMail management API did not become ready to seed {email} within ~10s.", last);
    }

    /// <summary>APPEND a message into the given account's folder (creating the folder if needed).</summary>
    public Task AppendAsync(string account, string folderName, string subject, string messageId)
        => AppendAsync(account, folderName, subject, messageId, "body of " + subject);

    /// <summary>APPEND a message with an explicit body (used by the security gate to embed a body sentinel).</summary>
    public async Task AppendAsync(string account, string folderName, string subject, string messageId, string body)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(Host, ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(account, MailPassword);
        var inbox = client.Inbox!;
        IMailFolder folder = inbox;
        if (!folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            try { folder = (await inbox.GetSubfolderAsync(folderName))!; }
            catch (FolderNotFoundException) { folder = (await inbox.CreateAsync(folderName, true))!; }
        }

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@local.test"));
        msg.To.Add(new MailboxAddress("Recipient", account));
        msg.Subject = subject;
        msg.MessageId = messageId;
        msg.Body = new TextPart("plain") { Text = body };

        await folder.OpenAsync(FolderAccess.ReadWrite);
        await folder.AppendAsync(new AppendRequest(msg, MailKit.MessageFlags.Seen, DateTimeOffset.UtcNow));
        await client.DisconnectAsync(true);
    }

    /// <summary>
    /// Empties every folder of an account (INBOX + all subfolders) by expunging its messages.
    /// Folders themselves are left in place — GreenMail 2.1.0 drops the connection on folder
    /// DELETE, and an empty folder fans out to zero batches anyway, so an id-scoped ledger count
    /// stays exact. Tests in the "pipeline" collection run sequentially, so resetting before each
    /// seed guarantees a migration only ever copies the current test's freshly-seeded messages.
    /// </summary>
    public async Task ResetMailboxAsync(string account)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(Host, ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(account, MailPassword);
        await EmptyFolderRecursiveAsync(client.Inbox!);
        await client.DisconnectAsync(true);
    }

    private static async Task EmptyFolderRecursiveAsync(IMailFolder folder)
    {
        await folder.OpenAsync(FolderAccess.ReadWrite);
        if (folder.Count > 0)
        {
            await folder.AddFlagsAsync(new UniqueIdRange(UniqueId.MinValue, UniqueId.MaxValue),
                MailKit.MessageFlags.Deleted, true);
            await folder.ExpungeAsync();
        }

        foreach (var child in await folder.GetSubfoldersAsync(false))
        {
            await EmptyFolderRecursiveAsync(child);
        }
    }

    /// <summary>Total message count across INBOX and all of its subfolders for an account.</summary>
    public async Task<int> CountAllAsync(string account)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(Host, ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(account, MailPassword);
        var inbox = client.Inbox!;
        var total = await CountFolderRecursiveAsync(inbox);
        await client.DisconnectAsync(true);
        return total;
    }

    /// <summary>Distinct Message-IDs across INBOX and all subfolders for an account.</summary>
    public async Task<HashSet<string>> MessageIdsAsync(string account)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using var client = new ImapClient();
        await client.ConnectAsync(Host, ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(account, MailPassword);
        await CollectIdsRecursiveAsync(client.Inbox!, ids);
        await client.DisconnectAsync(true);
        return ids;
    }

    // ── Security gate (Task 13) ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets both mailboxes, then seeds <paramref name="count"/> messages into one fresh per-run
    /// folder whose BODIES each contain <paramref name="sentinel"/>. Returns the seeded Message-IDs
    /// (without angle brackets) so the test can wait for the destination to receive them.
    /// </summary>
    public async Task<IReadOnlyList<string>> SeedSourceWithBodySentinelAsync(int count, string sentinel)
    {
        await ResetMailboxAsync(SrcEmail);
        await ResetMailboxAsync(DstEmail);
        var token = Guid.NewGuid().ToString("N")[..8];
        var folder = $"Mail-Sec-{token}";
        var ids = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var mid = $"<sec-{token}-{i}@local.test>";
            ids.Add(mid.Trim('<', '>'));
            // The sentinel lives ONLY in the body; the subject is generic. If any byte of the body
            // were persisted, the raw Postgres / disk scan would find the sentinel.
            await AppendAsync(SrcEmail, folder, $"sec-{token}-{i}",
                mid, $"line1 {sentinel} line2 — secret body content for {mid}");
        }

        return ids;
    }

    /// <summary>
    /// Resets both mailboxes, then seeds <paramref name="count"/>-1 healthy messages plus ONE poison
    /// message whose SUBJECT is "<paramref name="subjectSentinel"/> EMAIGRATOR_POISON" (so the
    /// FaultInjectingMessageHydrator throws → DLQ) and whose BODY contains
    /// <paramref name="bodySentinel"/>. Returns (healthyIds, poisonMessageId).
    /// </summary>
    public async Task<(IReadOnlyList<string> HealthyIds, string PoisonId)> SeedSourceWithOnePoisonSentinelAsync(
        int count, long _maxBytesIgnored, string bodySentinel, string subjectSentinel)
    {
        await ResetMailboxAsync(SrcEmail);
        await ResetMailboxAsync(DstEmail);
        var token = Guid.NewGuid().ToString("N")[..8];
        var folder = $"Mail-SecP-{token}";
        var healthy = new List<string>(Math.Max(0, count - 1));
        for (var i = 0; i < count - 1; i++)
        {
            var mid = $"<secp-{token}-{i}@local.test>";
            healthy.Add(mid.Trim('<', '>'));
            await AppendAsync(SrcEmail, folder, $"ok-{token}-{i}", mid);
        }

        var poisonMid = $"<secp-poison-{token}@local.test>";
        var poisonSubject = $"{subjectSentinel} {FaultInjectingMessageHydrator.PoisonMarker}";
        await AppendAsync(SrcEmail, folder, poisonSubject,
            poisonMid, $"poison body — {bodySentinel} — do not persist this");
        return (healthy, poisonMid.Trim('<', '>'));
    }

    /// <summary>
    /// Builds a REAL <see cref="RedisRateLimiter"/> bound to the fixture's live Redis, configured with
    /// a single bucket "graph:dest@biz.com" = <paramref name="spec"/>. Used to prove the limiter caps
    /// grants under concurrency.
    /// </summary>
    public RedisRateLimiter CreateRateLimiter(BucketSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var options = new RateLimitOptions
        {
            Buckets = new Dictionary<string, BucketSpec> { ["graph:dest@biz.com"] = spec },
        };
        return new RedisRateLimiter(RedisMultiplexer, Options.Create(options));
    }

    /// <summary>Live Redis multiplexer pointed at the fixture's container (lazily connected once).</summary>
    public IConnectionMultiplexer RedisMultiplexer =>
        _redisMux ??= ConnectionMultiplexer.Connect(RedisConnectionString);

    private ConnectionMultiplexer? _redisMux;

    private static async Task<int> CountFolderRecursiveAsync(IMailFolder folder)
    {
        var count = 0;
        await folder.OpenAsync(FolderAccess.ReadOnly);
        count += folder.Count;
        foreach (var child in await folder.GetSubfoldersAsync(false))
        {
            count += await CountFolderRecursiveAsync(child);
        }

        return count;
    }

    [SuppressMessage("Performance", "CA1851:Possible multiple enumerations of 'IEnumerable' collection",
        Justification = "Each Fetch result is enumerated once.")]
    private static async Task CollectIdsRecursiveAsync(IMailFolder folder, HashSet<string> ids)
    {
        await folder.OpenAsync(FolderAccess.ReadOnly);
        if (folder.Count > 0)
        {
            var summaries = await folder.FetchAsync(0, -1, MessageSummaryItems.Envelope);
            foreach (var s in summaries)
            {
                if (s.Envelope?.MessageId is { } mid)
                {
                    ids.Add(mid.Trim('<', '>'));
                }
            }
        }

        foreach (var child in await folder.GetSubfoldersAsync(false))
        {
            await CollectIdsRecursiveAsync(child, ids);
        }
    }
}

[CollectionDefinition("pipeline")]
public sealed class PipelineCollectionMarker : ICollectionFixture<EmaigratorPipelineFixture>;
