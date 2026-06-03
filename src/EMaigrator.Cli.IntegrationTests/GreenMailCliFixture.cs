using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace EMaigrator.Cli.IntegrationTests;

/// <summary>
/// Boots the full real-infra trio (Postgres + Redis + RabbitMQ) plus a GreenMail IMAP server holding
/// two mailboxes (source@ / dest@) and exposes everything the CLI end-to-end tests need. In
/// <see cref="InitializeAsync"/> it ALSO sets the complete EMAIGRATOR_-prefixed environment-variable
/// set the CLI host reads (so <c>CliHostBuilder.Build</c> binds to these live containers), and clears
/// every key it set in <see cref="DisposeAsync"/>. Started once per "cli-e2e" collection.
///
/// The GreenMail image/opts/wait-strategy/user-seeding mirror the PROVEN
/// <c>EMaigrator.Workers.IntegrationTests.EmaigratorPipelineFixture</c>; the plan's untested
/// GreenMail variant (auth.disabled + -Dgreenmail.users=...) does not start reliably in this repo.
/// </summary>
public sealed class GreenMailCliFixture : IAsyncLifetime
{
    public const string SourceUser = "source@greenmail.local";
    public const string DestUser = "dest@greenmail.local";
    public const string SourcePassword = "src-pass-123";
    public const string DestPassword = "dst-pass-456";

    private const int GreenMailApiPort = 8080;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("emaigrator").WithUsername("emaigrator").WithPassword("emaigrator").Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly RabbitMqContainer _rabbit =
        new RabbitMqBuilder("rabbitmq:3.13-management-alpine").Build();

    private IContainer _greenMail = null!;

    private readonly List<string> _envKeys = [];

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Part of the fixture's required public surface (the e2e tests read fx.Host as an instance member).")]
    public string Host => "127.0.0.1";
    public int ImapPort { get; private set; }
    public int ApiPort { get; private set; }

    public async Task InitializeAsync()
    {
        _greenMail = new ContainerBuilder("greenmail/standalone:2.1.0")
            .WithEnvironment("GREENMAIL_OPTS",
                "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.verbose")
            .WithPortBinding(3143, assignRandomHostPort: true)
            .WithPortBinding(3025, assignRandomHostPort: true)
            .WithPortBinding(GreenMailApiPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged(new Regex("Starting GreenMail API server")))
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _rabbit.StartAsync(),
            _greenMail.StartAsync());

        ImapPort = _greenMail.GetMappedPublicPort(3143);
        ApiPort = _greenMail.GetMappedPublicPort(GreenMailApiPort);

        await SeedUserAsync(SourceUser, SourcePassword);
        await SeedUserAsync(DestUser, DestPassword);

        var masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // The CLI host reads EMAIGRATOR_-prefixed env (prefix stripped, "__" -> ":"). Set the complete
        // set so AddInfrastructure (Infrastructure section), the secret store, the rate limiter, and the
        // in-process worker (AddEmaigratorWorkers: top-level Workers/Orchestration + ConnectionStrings:RabbitMq)
        // all bind to these live containers. The generous rate-limit buckets are ESSENTIAL — a throttle would
        // fault the batch to the DLQ and the run would never reach Completed.
        SetEnv("EMAIGRATOR_Infrastructure__PostgresConnectionString", _postgres.GetConnectionString());
        SetEnv("EMAIGRATOR_Infrastructure__RedisConnectionString", _redis.GetConnectionString());
        SetEnv("EMAIGRATOR_Infrastructure__RabbitMqConnectionString", _rabbit.GetConnectionString());
        SetEnv("EMAIGRATOR_ConnectionStrings__RabbitMq", _rabbit.GetConnectionString());
        SetEnv("EMAIGRATOR_Infrastructure__SecretStore__Mode", "LocalKey");
        SetEnv("EMAIGRATOR_Infrastructure__SecretStore__KeyRef", masterKey);
        SetEnv("EMAIGRATOR_Infrastructure__RateLimit__Buckets__imap__RefillPerSecond", "1000");
        SetEnv("EMAIGRATOR_Infrastructure__RateLimit__Buckets__imap__Burst", "1000");
        SetEnv("EMAIGRATOR_Infrastructure__RateLimit__Buckets__default__RefillPerSecond", "1000");
        SetEnv("EMAIGRATOR_Infrastructure__RateLimit__Buckets__default__Burst", "1000");
        SetEnv("EMAIGRATOR_Workers__UseInMemoryTransport", "false");
        SetEnv("EMAIGRATOR_Orchestration__BatchSize", "5");
        SetEnv("EMAIGRATOR_Orchestration__ConsumerPrefetch", "8");
        SetEnv("EMAIGRATOR_Orchestration__DlqRetryCount", "2");
        SetEnv("EMAIGRATOR_SECRET_FROM", SourcePassword);
        SetEnv("EMAIGRATOR_SECRET_TO", DestPassword);
    }

    public async Task DisposeAsync()
    {
        foreach (var key in _envKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        await _greenMail.DisposeAsync();
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask());
    }

    private void SetEnv(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _envKeys.Add(key);
    }

    private async Task SeedUserAsync(string email, string password)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{Host}:{ApiPort}") };
        var payload = $$"""{"email":"{{email}}","login":"{{email}}","password":"{{password}}"}""";
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
}

[CollectionDefinition("cli-e2e")]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection-definition marker; the 'Collection' suffix is idiomatic for ICollectionFixture markers.")]
public sealed class CliE2eCollection : ICollectionFixture<GreenMailCliFixture>;
