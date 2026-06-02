using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Google;
using Google.Apis.Requests;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Connectors.Gmail.Tests;

/// <summary>
/// Security Verification gate (Plan 06, Task 13). Each fact independently re-validates a named
/// security property of the Gmail connector and captures evidence via <see cref="ITestOutputHelper"/>:
/// least-privilege DWD scope, the service-account JSON key never spills to disk and is never exposed
/// via a public member, quota/auth errors never leak the SA identity or impersonated mailbox, no
/// credential reaches any provider-emitted console/exception text, the production endpoint is pinned
/// to TLS-only gmail.googleapis.com, and the DWD scope justification is documented.
/// </summary>
public class GmailSecurityVerificationTests
{
    private const string SaEmail = "emaigrator-test@test-project.iam.gserviceaccount.com";
    private const string Mailbox = "victim@example.com";

    private readonly ITestOutputHelper _out;
    public GmailSecurityVerificationTests(ITestOutputHelper output) => _out = output;

    private static GmailConnectionConfig BuildConfig()
    {
        var secrets = new SecretBundle(new Dictionary<string, string> { ["serviceAccountJson"] = TestServiceAccount.Json });
        var descriptor = new ConnectionDescriptor
        {
            Provider = new ProviderId("gmail"),
            Auth = AuthMethod.GmailServiceAccountDwd,
            Settings = new Dictionary<string, string> { ["accountEmail"] = Mailbox },
        };
        return GmailConnectionConfig.FromDescriptor(descriptor, secrets);
    }

    // ---- (a) Minimal scope, justified ----
    [Fact]
    public void Scope_IsMinimalMailGoogleComOnly()
    {
        GmailServiceFactory.RequiredScopes.Should().Equal(new[] { "https://mail.google.com/" });
        GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("drive", StringComparison.Ordinal));
        GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("calendar", StringComparison.Ordinal));
        GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("gmail.readonly", StringComparison.Ordinal));
        GmailServiceFactory.RequiredScopes.Should().NotContain(s => s.Contains("gmail.modify", StringComparison.Ordinal));

        _out.WriteLine($"RequiredScopes: [{string.Join(", ", GmailServiceFactory.RequiredScopes)}]");
    }

    // ---- (b) Key never on disk ----
    [Fact]
    public void ServiceConstruction_WritesNoKeyToDisk()
    {
        var config = BuildConfig();

        var tempBefore = Directory.GetFiles(Path.GetTempPath()).Length;
        var cwdBefore = Directory.GetFiles(Directory.GetCurrentDirectory()).Length;

        using var service = GmailServiceFactory.Create(config);

        var tempAfter = Directory.GetFiles(Path.GetTempPath()).Length;
        var cwdAfter = Directory.GetFiles(Directory.GetCurrentDirectory()).Length;

        _out.WriteLine($"Temp file count before/after: {tempBefore}/{tempAfter}; cwd before/after: {cwdBefore}/{cwdAfter}");

        tempAfter.Should().Be(tempBefore, "the SA JSON must be parsed in-memory, never spilled to a temp file");
        cwdAfter.Should().Be(cwdBefore, "the SA JSON must never be written to the working directory");
    }

    // ---- (c) Key not exposed publicly ----
    [Fact]
    public void Config_NeverExposesPrivateKeyViaPublicMembers()
    {
        var config = BuildConfig();

        // Walk every public string-returning property and field; none may surface the SA JSON or PEM.
        var stringValues = new List<string?>();
        stringValues.AddRange(config.GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.GetGetMethod()?.IsPublic == true)
            .Select(p => (string?)p.GetValue(config)));
        stringValues.AddRange(config.GetType().GetFields()
            .Where(f => f.FieldType == typeof(string) && f.IsPublic)
            .Select(f => (string?)f.GetValue(config)));

        var exposed = stringValues.Any(v => v != null
            && (v.Contains("PRIVATE KEY", StringComparison.Ordinal)
                || v.Contains("\"private_key\"", StringComparison.Ordinal)));

        _out.WriteLine($"Public string members inspected: {stringValues.Count}; private-key exposed: {exposed}");
        exposed.Should().BeFalse("the service-account JSON / private key must never be reachable via a public member");
    }

    // ---- (d) No SA identity / mailbox in error surface ----
    [Theory]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "quotaExceeded", "gmail:403:quotaExceeded")]
    [InlineData(System.Net.HttpStatusCode.Unauthorized, "authError", "gmail:401:authError")]
    public void ErrorSignature_LeaksNeitherMailboxNorServiceAccount(
        System.Net.HttpStatusCode status, string reason, string expected)
    {
        var msg = $"User rate limit exceeded for {Mailbox}; service account {SaEmail}";
        var err = new RequestError
        {
            Code = (int)status,
            Message = msg,
            Errors = new List<SingleError> { new SingleError { Reason = reason, Message = msg } },
        };
        var ex = new GoogleApiException("gmail", msg) { HttpStatusCode = status, Error = err };

        var sig = GmailErrorNormalizer.Normalize(ex);

        _out.WriteLine($"Raw message: {msg}");
        _out.WriteLine($"Normalized signature for {(int)status}/{reason}: {sig}");

        sig.Should().Be(expected);
        sig.Should().NotContain("@", "a leaked '@' would imply an address rode through into the signature");
        sig.Should().NotContain("victim", "the impersonated mailbox local-part must never appear in the signature");
        sig.Should().NotContain("gserviceaccount", "the service-account identity must never appear in the signature");
    }

    // ---- (e) No credential in any log/exception text emitted by the providers ----
    [Fact]
    public async Task TestConnectionFailure_EmitsNoCredentialToConsole()
    {
        using var fx = new GmailWireMockFixture();
        // 401 body deliberately embeds BOTH the impersonated mailbox and the SA email; the test then
        // proves neither reaches the console while the provider normalizes the failure.
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(401)
              .WithHeader("Content-Type", "application/json")
              .WithBody($"{{\"error\":{{\"code\":401,\"message\":\"Invalid Credentials for {Mailbox} via {SaEmail}\",\"errors\":[{{\"reason\":\"authError\"}}]}}}}"));

        var origOut = Console.Out;
        var origErr = Console.Error;
        var rec = new RecordingTextWriter();
        Console.SetOut(rec);
        Console.SetError(rec);
        try
        {
            await using var src = new GmailSourceProvider(fx.CreateService(), "me");
            var result = await src.TestConnectionAsync(CancellationToken.None);

            result.Ok.Should().BeFalse();
            result.ErrorCode.Should().Be("gmail:401:authError");
            (result.RawDetail ?? "").Should().NotContain("@", "the caller-facing detail must carry no address");
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        _out.WriteLine($"Captured console length: {rec.Captured.Length} chars");
        rec.Captured.Should().NotContain(SaEmail, "the service-account email must never reach the console");
        rec.Captured.Should().NotContain(Mailbox, "the impersonated mailbox must never reach the console");
        rec.Captured.Should().NotContain("PRIVATE KEY", "the private key must never reach the console");
    }

    // ---- (f) TLS enforced / endpoint pinned ----
    [Fact]
    public void ProductionService_PinsHttpsGoogleEndpoint()
    {
        var config = BuildConfig();

        using var service = GmailServiceFactory.Create(config);

        var baseUri = new Uri(service.BaseUri);
        _out.WriteLine($"Production BaseUri: {service.BaseUri}");

        baseUri.Scheme.Should().Be("https", "TLS must be enforced; no cleartext downgrade");
        baseUri.Host.Should().Be("gmail.googleapis.com", "the endpoint must be pinned and not caller-controllable");
    }

    // ---- (g) DWD justification documented ----
    [Fact]
    public void Docs_RecordScopeJustification()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repo docs/ directory must resolve from the test output path");
        var docPath = Path.Combine(dir!.FullName, "docs", "connectors", "gmail-testing.md");
        var doc = File.ReadAllText(docPath);

        _out.WriteLine($"Scope-justification doc: {docPath}");
        doc.Should().Contain("https://mail.google.com/", "the minimal DWD scope must be documented");
        doc.Should().Contain("least privilege", "the scope must be justified as least privilege");
    }
}
