using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit.Abstractions;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>
/// Security Verification gate (Plan 05, Task 12). Each fact asserts a security property of the
/// Graph connector and captures evidence via <see cref="ITestOutputHelper"/>: no secret/token in
/// logs, least-privilege scope, no on-disk token cache, throttling that leaks no tenant/secret,
/// TLS-only / no-foreign-host endpoints, and a redacted config ToString. Static-source assertions
/// read the real connector .cs files from disk (the enumerated set is asserted non-empty so an
/// accidentally-empty scan cannot become a false pass).
/// </summary>
public class GraphSecurityVerificationTests
{
    private const string Secret = "ULTRA-SECRET-CLIENT-VALUE-9f3a";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Account = "user@contoso.onmicrosoft.com";

    // Fixture inlined as a constant rather than loaded from disk: the test csproj does not copy a
    // Fixtures\** item to the output directory (established by GraphDestinationProviderTests), so a
    // File.ReadAllText(AppContext.BaseDirectory/Fixtures/...) would fail at runtime. Content matches
    // the folders_list.json shape; the "Projects" child of "Inbox" resolves to id "projects-id".
    private const string FoldersListJson = """
        {
          "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('user')/mailFolders",
          "value": [
            { "id": "inbox-id", "displayName": "Inbox", "parentFolderId": "msgfolderroot", "totalItemCount": 2, "childFolderCount": 1 },
            { "id": "projects-id", "displayName": "Projects", "parentFolderId": "inbox-id", "totalItemCount": 1, "childFolderCount": 0 },
            { "id": "sent-id", "displayName": "Sent Items", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 0 },
            { "id": "drafts-id", "displayName": "Drafts", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 0 }
          ]
        }
        """;

    private readonly ITestOutputHelper _out;
    public GraphSecurityVerificationTests(ITestOutputHelper output) => _out = output;

    private static string SourceDir =>
        // tests bin: .../src/EMaigrator.Connectors.Graph.Tests/bin/<cfg>/net10.0
        // climb 4x .. -> .../src, then into the connector project.
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "EMaigrator.Connectors.Graph"));

    private static List<string> ConnectorSources() =>
        Directory.EnumerateFiles(SourceDir, "*.cs", SearchOption.AllDirectories)
            // Exclude generated obj/** artifacts so the scan reflects only authored connector code.
            .Where(p => !p.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            .ToList();

    // The static-source scans are only meaningful if they actually read files. Resolve + assert the
    // .cs list is non-empty so an empty scan can never be a silent false pass.
    private List<string> ResolvedSources()
    {
        var sources = ConnectorSources();
        _out.WriteLine($"SourceDir: {SourceDir}");
        _out.WriteLine(FormattableString.Invariant($"Connector .cs files scanned: {sources.Count}"));
        Directory.Exists(SourceDir).Should().BeTrue($"connector source dir must resolve at runtime ({SourceDir})");
        sources.Should().NotBeEmpty("the static-source scan must read real connector .cs files (an empty scan is a false pass)");
        return sources;
    }

    // ---- 1. No secret/token in captured logs ----
    [Fact]
    public async Task No_client_secret_appears_in_captured_logs()
    {
        var captured = new ConcurrentBag<string>();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new CapturingLoggerProvider(captured)));

        using var server = WireMockServer.Start();
        // A read (403) and a write (429) that both fail, exercising the connector's error path.
        server.Given(Request.Create().WithPath("/v1.0/users/" + Account + "/mailFolders").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(403)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody($"{{\"error\":{{\"code\":\"ErrorAccessDenied\",\"message\":\"denied for tenant {Tenant} secret {Secret}\"}}}}"));

        var logger = loggerFactory.CreateLogger("graph-test");

        // TestConnection drives a failing read; the connector must surface only the credential-free
        // signature, and any caller logging that result must never observe the secret/token/tenant.
        var source = new GraphSourceProvider(GraphTestClientFactory.Create(server.Url!), Account);
        var testResult = await source.TestConnectionAsync(CancellationToken.None);
        // CA1848 (LoggerMessage delegates) is a runtime-perf rule irrelevant to this gate: the point
        // is to populate the capturing sink with caller-side logging and prove no credential surfaces.
#pragma warning disable CA1848
        logger.LogWarning("TestConnection failed with code {Code}", testResult.ErrorCode);
        logger.BeginScope("connection {Code}", testResult.ErrorCode)?.Dispose();
#pragma warning restore CA1848

        // A throttled (429) write, with the secret + tenant embedded in the server body, also fails.
        var server2 = server;
        server2.Given(Request.Create().WithPath("/v1.0/users/" + Account + "/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));
        server2.Given(Request.Create()
                   .WithPath("/v1.0/users/" + Account + "/mailFolders/projects-id/messages").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(429)
                   .WithHeader("Content-Type", "application/json").WithHeader("Retry-After", "9")
                   .WithBody($"{{\"error\":{{\"code\":\"errorThrottledRequest\",\"message\":\"tenant {Tenant} secret {Secret}\"}}}}"));

        var dest = new GraphDestinationProvider(GraphTestClientFactory.Create(server.Url!), Account);
        var writeResult = await dest.WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"),
            new CanonicalMessage
            {
                IdentityKey = "mid:<x@contoso.com>",
                MessageId = "<x@contoso.com>",
                InternalDate = DateTimeOffset.UnixEpoch,
                OpenContentAsync = ct => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])),
            },
            CancellationToken.None);
#pragma warning disable CA1848
        logger.LogWarning("Write failed with code {Code}", writeResult.ErrorCode);
#pragma warning restore CA1848

        var all = string.Join("\n", captured);
        _out.WriteLine(FormattableString.Invariant($"Scanned {captured.Count} log entries."));
        all.Should().NotContain(Secret, "the client secret must never appear in any log entry or scope");
        all.Should().NotContain(Tenant, "the tenant id must never appear in any log entry or scope");
    }

    // ---- 2. Least-privilege scope (runtime + static source) ----
    [Fact]
    public void Scope_is_least_privilege_at_runtime()
    {
        GraphClientFactory.GraphScopes.Should().Equal("https://graph.microsoft.com/.default");
        GraphConnectionConfig.GraphScopes.Should().Equal("https://graph.microsoft.com/.default");
    }

    [Fact]
    public void No_broad_or_send_scopes_in_connector_sources()
    {
        var sendMatches = new List<string>();
        var broadReadMatches = new List<string>();
        foreach (var file in ResolvedSources())
        {
            var text = File.ReadAllText(file);
            if (text.Contains("Mail.Send", StringComparison.Ordinal)) sendMatches.Add(file);
            // The literal broad scope "Mail.Read" (all mailboxes) must not be requested as a scope string.
            if (text.Contains("\"Mail.Read\"", StringComparison.Ordinal)) broadReadMatches.Add(file);
        }

        sendMatches.Should().BeEmpty("connector must never request send permission");
        broadReadMatches.Should().BeEmpty("connector must not request broad Mail.Read scope");
        _out.WriteLine(FormattableString.Invariant(
            $"Mail.Send matches: {sendMatches.Count}; \"Mail.Read\" matches: {broadReadMatches.Count} (both must be empty)."));
    }

    // ---- 3. Token cache never persisted to disk ----
    [Fact]
    public void Token_cache_persistence_is_null_at_runtime()
    {
        GraphClientFactory.BuildCredentialOptions().TokenCachePersistenceOptions.Should().BeNull();
    }

    [Fact]
    public void Token_cache_persistence_never_assigned_non_null_in_sources()
    {
        var matches = new List<string>();
        foreach (var file in ResolvedSources())
        {
            var text = File.ReadAllText(file);
            // Forbid constructing a persistence options object anywhere in the connector.
            if (text.Contains("new TokenCachePersistenceOptions", StringComparison.Ordinal)) matches.Add(file);
        }

        matches.Should().BeEmpty("token cache must never be persisted to disk");
        _out.WriteLine(FormattableString.Invariant(
            $"new TokenCachePersistenceOptions matches: {matches.Count} (must be empty)."));
    }

    // ---- 4. 429 throttling does not leak tenant/secret ----
    [Fact]
    public void Throttled_signature_contains_no_tenant_or_secret()
    {
        var err = new ODataError
        {
            ResponseStatusCode = 429,
            Error = new MainError
            {
                Code = "errorThrottledRequest",
                Message = $"throttled tenant {Tenant} secret {Secret} account {Account}",
            },
            ResponseHeaders = new Microsoft.Kiota.Abstractions.RequestHeaders { { "Retry-After", "12" } },
        };

        var n = GraphErrorNormalizer.Normalize(err);
        _out.WriteLine(FormattableString.Invariant($"Signature: {n.Signature}; RetryAfter: {n.RetryAfter}"));

        n.Signature.Should().Be("graph:429:throttled");
        n.Signature.Should().NotContain(Tenant);
        n.Signature.Should().NotContain(Secret);
        n.Signature.Should().NotContain(Account);
        n.RetryAfter.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task WriteMessage_throttled_error_code_leaks_nothing()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1.0/users/" + Account + "/mailFolders").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));
        server.Given(Request.Create()
                  .WithPath("/v1.0/users/" + Account + "/mailFolders/projects-id/messages").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(429)
                  .WithHeader("Content-Type", "application/json").WithHeader("Retry-After", "9")
                  .WithBody($"{{\"error\":{{\"code\":\"errorThrottledRequest\",\"message\":\"tenant {Tenant}\"}}}}"));

        var dest = new GraphDestinationProvider(GraphTestClientFactory.Create(server.Url!), Account);
        var msg = new CanonicalMessage
        {
            IdentityKey = "mid:<x@contoso.com>",
            MessageId = "<x@contoso.com>",
            InternalDate = DateTimeOffset.UnixEpoch,
            OpenContentAsync = ct => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])),
        };

        var result = await dest.WriteMessageAsync(FolderPath.Parse("Inbox/Projects"), msg, CancellationToken.None);
        _out.WriteLine($"Throttled WriteResult.ErrorCode: {result.ErrorCode}");

        result.Written.Should().BeFalse();
        result.ErrorCode.Should().Be("graph:429:throttled");
        result.ErrorCode.Should().NotContain(Tenant);
        result.ErrorCode.Should().NotContain(Secret);
        result.ErrorCode.Should().NotContain(Account);
    }

    // ---- 5. Config ToString redaction ----
    [Fact]
    public void Config_ToString_redacts_secret()
    {
        var descriptor = new ConnectionDescriptor
        {
            Provider = new ProviderId("graph"),
            Auth = AuthMethod.GraphAppOAuth,
            Settings = new Dictionary<string, string>
            {
                ["tenantId"] = Tenant,
                ["clientId"] = "c",
                ["accountEmail"] = Account,
            },
            SecretRef = "ref",
        };
        var cfg = GraphConnectionConfig.FromDescriptor(
            descriptor, new SecretBundle(new Dictionary<string, string> { ["clientSecret"] = Secret }));

        var rendered = cfg.ToString();
        _out.WriteLine($"Config.ToString(): {rendered}");
        rendered.Should().NotContain(Secret);
        rendered.Should().Contain("REDACTED");
    }

    // ---- 6. TLS enforced + no arbitrary-host exfiltration ----
    [Fact]
    public void Production_client_targets_only_https_graph_endpoint()
    {
        var config = new GraphConnectionConfig
        {
            TenantId = Tenant,
            ClientId = "c",
            AccountEmail = Account,
            ClientSecret = Secret,
        };

        var client = GraphClientFactory.Build(config);
        var baseUrl = client.RequestAdapter.BaseUrl ?? "";
        _out.WriteLine($"Production BaseUrl: {baseUrl}");

        baseUrl.Should().StartWith("https://graph.microsoft.com");
    }

    [Fact]
    public void No_plaintext_http_or_foreign_host_in_connector_sources()
    {
        var httpMatches = new List<string>();
        var foreignHosts = new List<string>();
        foreach (var file in ResolvedSources())
        {
            var text = File.ReadAllText(file);
            if (text.Contains("http://", StringComparison.Ordinal)) httpMatches.Add(file);
            // The only host literal permitted in the connector is the official Graph endpoint.
            foreach (Match m in Regex.Matches(text, "https://[\\w.-]+"))
            {
                if (!m.Value.StartsWith("https://graph.microsoft.com", StringComparison.Ordinal))
                {
                    foreignHosts.Add($"{file}: {m.Value}");
                }
            }
        }

        httpMatches.Should().BeEmpty("connector must never use a plaintext (non-TLS) endpoint");
        foreignHosts.Should().BeEmpty("connector must not point at any host other than graph.microsoft.com");
        _out.WriteLine(FormattableString.Invariant(
            $"http:// matches: {httpMatches.Count}; foreign https host matches: {foreignHosts.Count} (both must be empty)."));
    }

    private sealed class CapturingLoggerProvider(ConcurrentBag<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentBag<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                sink.Add(state.ToString() ?? "");
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => sink.Add(formatter(state, exception));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
