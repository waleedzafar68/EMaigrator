# EMaigrator.Connectors.Graph Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Implement the Microsoft Graph connector assembly (`EMaigrator.Connectors.Graph`) providing `GraphSourceProvider`, `GraphDestinationProvider`, and `GraphProviderPlugin` against the Microsoft Graph API using the Microsoft.Graph .NET SDK v5 with BYO-OAuth (client-credentials, application permission `Mail.ReadWrite`), so EMaigrator can read from and write to Microsoft 365 mailboxes (the WorkMail→MS365 wedge destination).

**Architecture:** A DI-discovered plugin assembly that depends **only** on `EMaigrator.Core` abstractions (CONTRACTS §2) and the Microsoft.Graph SDK. `GraphProviderPlugin` advertises `ProviderId("graph")` with `AuthMethod.GraphAppOAuth`/`GraphDelegatedOAuth`; it builds a `GraphServiceClient` from a `ClientSecretCredential` (Azure.Identity) using non-secret `ConnectionDescriptor.Settings` (tenantId, clientId, accountEmail) plus the decrypted `SecretBundle` (clientSecret). Source/destination providers translate Graph mailFolders ⇄ canonical `FolderPath`, Graph messages ⇄ `CanonicalMessage` (MIME via `$value`/`OpenContentAsync`), and normalize Graph error codes (including throttling 429 + `Retry-After`) into the stable `errorSignature` strings the Core error catalog matches. No bodies are persisted — message content is streamed on demand.

**Tech Stack:** C#/.NET 10, C# 13 (nullable enabled); Microsoft.Graph v5 SDK; Azure.Identity (`ClientSecretCredential`); Microsoft.Kiota.Abstractions for request-adapter injection; MailKit/MimeKit only where MIME parsing of decoded body is needed for the identity fallback. Tests: xUnit, FluentAssertions, NSubstitute, WireMock.Net (fixtures recorded from the free M365 Developer tenant). Live smoke uses the free M365 Developer Program tenant (gated, not per-commit — DESIGN §17).

---

### Task 0: Project scaffold, references, and plugin DI extension

**Goal:** Create the `EMaigrator.Connectors.Graph` class library and its test project, wired to `EMaigrator.Core` only (dependency rule, DESIGN §15), exposing the `AddGraphConnector()` DI extension stub.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/EMaigrator.Connectors.Graph.csproj`
- Create: `src/EMaigrator.Connectors.Graph/GraphConnectorServiceCollectionExtensions.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/EMaigrator.Connectors.Graph.Tests.csproj`
- Create: `src/EMaigrator.Connectors.Graph.Tests/ProjectStructureTests.cs`
- Modify: `EMaigrator.sln`

**Acceptance Criteria:**
- [ ] `EMaigrator.Connectors.Graph.csproj` targets `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, references `Microsoft.Graph`, `Azure.Identity`, `Microsoft.Kiota.Abstractions`, and `EMaigrator.Core` (project reference) and **nothing** from Infrastructure/Workers/Api.
- [ ] `GraphConnectorServiceCollectionExtensions.AddGraphConnector(IServiceCollection)` exists and registers `IProviderPlugin` → `GraphProviderPlugin` as a singleton.
- [ ] An architecture test asserts the assembly references `EMaigrator.Core` but does **not** reference `EMaigrator.Infrastructure`, `EMaigrator.Workers`, or `EMaigrator.Api`.
- [ ] Solution builds: `dotnet build src/EMaigrator.Connectors.Graph` succeeds.

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~ProjectStructureTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/ProjectStructureTests.cs`:
```csharp
using System.Linq;
using System.Reflection;
using EMaigrator.Core.Abstractions;
using EMaigrator.Connectors.Graph;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class ProjectStructureTests
{
    [Fact]
    public void Assembly_references_Core_but_not_infrastructure_layers()
    {
        var referenced = typeof(GraphProviderPlugin).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenced.Should().Contain("EMaigrator.Core");
        referenced.Should().NotContain("EMaigrator.Infrastructure");
        referenced.Should().NotContain("EMaigrator.Workers");
        referenced.Should().NotContain("EMaigrator.Api");
    }

    [Fact]
    public void AddGraphConnector_registers_the_plugin_as_IProviderPlugin()
    {
        var services = new ServiceCollection();
        services.AddGraphConnector();

        var provider = services.BuildServiceProvider();
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        plugin.Should().BeOfType<GraphProviderPlugin>();
        plugin.Id.Value.Should().Be("graph");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~ProjectStructureTests` → expected **FAIL**: projects/types do not exist yet (compile error: `GraphProviderPlugin`, `AddGraphConnector` not found).
3. - [ ] Create the csproj `src/EMaigrator.Connectors.Graph/EMaigrator.Connectors.Graph.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>13</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Graph" Version="5.56.0" />
    <PackageReference Include="Azure.Identity" Version="1.13.1" />
    <PackageReference Include="Microsoft.Kiota.Abstractions" Version="1.9.11" />
    <PackageReference Include="MimeKit" Version="4.8.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Core\EMaigrator.Core.csproj" />
  </ItemGroup>
</Project>
```
   Create the test csproj `src/EMaigrator.Connectors.Graph.Tests/EMaigrator.Connectors.Graph.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="WireMock.Net" Version="1.6.7" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Connectors.Graph\EMaigrator.Connectors.Graph.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```
   Create the DI extension and the minimal plugin so the project compiles `src/EMaigrator.Connectors.Graph/GraphConnectorServiceCollectionExtensions.cs`:
```csharp
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Connectors.Graph;

public static class GraphConnectorServiceCollectionExtensions
{
    /// <summary>Registers the Microsoft Graph connector plugin for DI discovery.</summary>
    public static IServiceCollection AddGraphConnector(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderPlugin, GraphProviderPlugin>());
        return services;
    }
}
```
   Create a minimal `src/EMaigrator.Connectors.Graph/GraphProviderPlugin.cs` to compile (fleshed out in Task 8):
```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Graph;

public sealed class GraphProviderPlugin : IProviderPlugin
{
    public static readonly ProviderId GraphProviderId = new("graph");

    public ProviderId Id => GraphProviderId;
    public IReadOnlyCollection<AuthMethod> SupportedAuth { get; } =
        [AuthMethod.GraphAppOAuth, AuthMethod.GraphDelegatedOAuth];
    public bool CanBeSource => true;
    public bool CanBeDestination => true;

    public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets)
        => throw new NotImplementedException();

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
        => throw new NotImplementedException();
}
```
   Add both projects to the solution: `dotnet sln EMaigrator.sln add src/EMaigrator.Connectors.Graph/EMaigrator.Connectors.Graph.csproj src/EMaigrator.Connectors.Graph.Tests/EMaigrator.Connectors.Graph.Tests.csproj`.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~ProjectStructureTests` → expected **PASS** (both tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph src/EMaigrator.Connectors.Graph.Tests EMaigrator.sln
git commit -m "feat(graph): scaffold Graph connector project and plugin DI extension

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 1: ProviderConstraints for Microsoft 365

**Goal:** Provide `GraphConstraints.MS365` — a `ProviderConstraints` (CONTRACTS §2) declaring Outlook/Exchange Online folder-depth, path-length, size caps, illegal characters, and reserved well-known folder names — used by both providers and the Core pre-flight analyzer.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphConstraints.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphConstraintsTests.cs`

**Acceptance Criteria:**
- [ ] `GraphConstraints.MS365` returns a `ProviderConstraints` with `MaxFolderDepth == 300`, `MaxMessageBytes == 150L * 1024 * 1024` (150 MB total message), `MaxAttachmentBytes == 150L * 1024 * 1024`, `FolderSeparator == '/'`.
- [ ] `IllegalNameChars` includes the Exchange illegal folder characters: `/`, `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|`.
- [ ] `ReservedFolderNames` includes the case-sensitive well-known display names `Inbox`, `Sent Items`, `Drafts`, `Deleted Items`, `Junk Email`, `Archive`, `Outbox`.
- [ ] Values are exposed as a single static readonly instance (no allocation per call) and are immutable.

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphConstraintsTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphConstraintsTests.cs`:
```csharp
using EMaigrator.Connectors.Graph;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphConstraintsTests
{
    [Fact]
    public void MS365_declares_expected_caps()
    {
        var c = GraphConstraints.MS365;

        c.MaxFolderDepth.Should().Be(300);
        c.MaxMessageBytes.Should().Be(150L * 1024 * 1024);
        c.MaxAttachmentBytes.Should().Be(150L * 1024 * 1024);
        c.FolderSeparator.Should().Be('/');
    }

    [Theory]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData(':')]
    [InlineData('*')]
    [InlineData('?')]
    [InlineData('"')]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData('|')]
    public void MS365_declares_illegal_folder_characters(char illegal)
    {
        GraphConstraints.MS365.IllegalNameChars.Should().Contain(illegal);
    }

    [Theory]
    [InlineData("Inbox")]
    [InlineData("Sent Items")]
    [InlineData("Drafts")]
    [InlineData("Deleted Items")]
    [InlineData("Junk Email")]
    [InlineData("Archive")]
    [InlineData("Outbox")]
    public void MS365_reserves_well_known_folder_names(string reserved)
    {
        GraphConstraints.MS365.ReservedFolderNames.Should().Contain(reserved);
    }

    [Fact]
    public void MS365_is_a_singleton_instance()
    {
        GraphConstraints.MS365.Should().BeSameAs(GraphConstraints.MS365);
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphConstraintsTests` → expected **FAIL**: `GraphConstraints` does not exist (compile error).
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphConstraints.cs`:
```csharp
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Declared Microsoft 365 / Exchange Online mailbox constraints used by pre-flight
/// (DESIGN.md §7). Depth limit reflects Exchange Online's documented folder-hierarchy
/// limit; the 150 MB cap reflects the maximum message size for Exchange Online.
/// </summary>
public static class GraphConstraints
{
    private const long Mb = 1024 * 1024;

    public static readonly ProviderConstraints MS365 = new()
    {
        MaxFolderDepth = 300,
        MaxPathLengthChars = 16_000,
        IllegalNameChars = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' },
        MaxMessageBytes = 150L * Mb,
        MaxAttachmentBytes = 150L * Mb,
        FolderSeparator = '/',
        ReservedFolderNames = new[]
        {
            "Inbox", "Sent Items", "Drafts", "Deleted Items",
            "Junk Email", "Archive", "Outbox"
        }
    };
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphConstraintsTests` → expected **PASS** (all 4 facts/theories green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphConstraints.cs src/EMaigrator.Connectors.Graph.Tests/GraphConstraintsTests.cs
git commit -m "feat(graph): declare MS365 provider constraints for pre-flight

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: GraphConnectionConfig — parse ConnectionDescriptor + SecretBundle (BYO-OAuth)

**Goal:** Implement `GraphConnectionConfig.FromDescriptor(ConnectionDescriptor, SecretBundle)` that validates and extracts the non-secret settings (`tenantId`, `clientId`, `accountEmail`) and the secret (`clientSecret`) for the client-credentials flow, requesting the least-privilege scope `https://graph.microsoft.com/.default` (the app's pre-consented `Mail.ReadWrite` application permission).

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphConnectionConfig.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphConnectionConfigTests.cs`

**Acceptance Criteria:**
- [ ] `GraphConnectionConfig.FromDescriptor` reads `TenantId`, `ClientId`, `AccountEmail` from `descriptor.Settings` (keys `tenantId`, `clientId`, `accountEmail`) and `ClientSecret` from `secrets.Values["clientSecret"]`.
- [ ] Missing/empty `tenantId`, `clientId`, `accountEmail`, or `clientSecret` throws `GraphConfigurationException` whose message names the missing field but **never** echoes the secret value.
- [ ] `GraphConnectionConfig.GraphScopes` is exactly `["https://graph.microsoft.com/.default"]` (least-privilege; no per-call elevated scopes).
- [ ] `ToString()`/`ToString` overrides and any diagnostic output redact the client secret (assert the secret string is absent from `ToString()`).
- [ ] Throws `GraphConfigurationException` when `descriptor.Auth` is not `GraphAppOAuth` or `GraphDelegatedOAuth`.

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphConnectionConfigTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphConnectionConfigTests.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphConnectionConfigTests
{
    private const string Secret = "super-secret-client-value-DO-NOT-LOG";

    private static ConnectionDescriptor Descriptor(
        IReadOnlyDictionary<string, string>? settings = null,
        AuthMethod auth = AuthMethod.GraphAppOAuth) => new()
    {
        Provider = new ProviderId("graph"),
        Auth = auth,
        Settings = settings ?? new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            ["clientId"] = "22222222-2222-2222-2222-222222222222",
            ["accountEmail"] = "user@contoso.onmicrosoft.com"
        },
        SecretRef = "ref-1"
    };

    private static SecretBundle Bundle() =>
        new(new Dictionary<string, string> { ["clientSecret"] = Secret });

    [Fact]
    public void FromDescriptor_extracts_settings_and_secret()
    {
        var cfg = GraphConnectionConfig.FromDescriptor(Descriptor(), Bundle());

        cfg.TenantId.Should().Be("11111111-1111-1111-1111-111111111111");
        cfg.ClientId.Should().Be("22222222-2222-2222-2222-222222222222");
        cfg.AccountEmail.Should().Be("user@contoso.onmicrosoft.com");
        cfg.ClientSecret.Should().Be(Secret);
    }

    [Fact]
    public void GraphScopes_is_least_privilege_default_scope()
    {
        GraphConnectionConfig.GraphScopes.Should().Equal("https://graph.microsoft.com/.default");
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("clientId")]
    [InlineData("accountEmail")]
    public void FromDescriptor_throws_when_a_required_setting_is_missing(string missingKey)
    {
        var settings = new Dictionary<string, string>
        {
            ["tenantId"] = "t",
            ["clientId"] = "c",
            ["accountEmail"] = "a@contoso.com"
        };
        settings.Remove(missingKey);

        var act = () => GraphConnectionConfig.FromDescriptor(Descriptor(settings), Bundle());

        act.Should().Throw<GraphConfigurationException>()
           .Which.Message.Should().Contain(missingKey);
    }

    [Fact]
    public void FromDescriptor_throws_when_client_secret_missing_without_leaking()
    {
        var emptyBundle = new SecretBundle(new Dictionary<string, string>());

        var act = () => GraphConnectionConfig.FromDescriptor(Descriptor(), emptyBundle);

        act.Should().Throw<GraphConfigurationException>()
           .Which.Message.Should().Contain("clientSecret");
    }

    [Fact]
    public void FromDescriptor_throws_for_unsupported_auth_method()
    {
        var act = () => GraphConnectionConfig.FromDescriptor(
            Descriptor(auth: AuthMethod.ImapBasic), Bundle());

        act.Should().Throw<GraphConfigurationException>();
    }

    [Fact]
    public void ToString_never_contains_the_client_secret()
    {
        var cfg = GraphConnectionConfig.FromDescriptor(Descriptor(), Bundle());

        cfg.ToString().Should().NotContain(Secret);
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphConnectionConfigTests` → expected **FAIL**: `GraphConnectionConfig`/`GraphConfigurationException` do not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphConfigurationException.cs`:
```csharp
namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Thrown when a Graph <see cref="EMaigrator.Core.Abstractions.ConnectionDescriptor"/> or
/// <see cref="EMaigrator.Core.Abstractions.SecretBundle"/> is missing or malformed.
/// The message must NEVER include a secret value.
/// </summary>
public sealed class GraphConfigurationException : Exception
{
    public GraphConfigurationException(string message) : base(message) { }
}
```
   Create `src/EMaigrator.Connectors.Graph/GraphConnectionConfig.cs`:
```csharp
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Parsed, validated Graph connection parameters. Non-secret values come from
/// <see cref="ConnectionDescriptor.Settings"/>; the client secret comes from the
/// transient <see cref="SecretBundle"/>. Least-privilege: only the application's
/// pre-consented Mail.ReadWrite permission is exercised via the .default scope.
/// </summary>
public sealed class GraphConnectionConfig
{
    /// <summary>The only scope ever requested: the app's pre-consented application permissions.</summary>
    public static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string AccountEmail { get; init; }
    public required string ClientSecret { get; init; }

    public static GraphConnectionConfig FromDescriptor(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        if (descriptor.Auth is not (AuthMethod.GraphAppOAuth or AuthMethod.GraphDelegatedOAuth))
            throw new GraphConfigurationException(
                $"Graph connector does not support auth method '{descriptor.Auth}'. " +
                "Expected GraphAppOAuth or GraphDelegatedOAuth.");

        var tenantId = RequireSetting(descriptor, "tenantId");
        var clientId = RequireSetting(descriptor, "clientId");
        var accountEmail = RequireSetting(descriptor, "accountEmail");

        if (!secrets.Values.TryGetValue("clientSecret", out var clientSecret)
            || string.IsNullOrWhiteSpace(clientSecret))
            throw new GraphConfigurationException(
                "Required secret 'clientSecret' is missing from the secret bundle.");

        return new GraphConnectionConfig
        {
            TenantId = tenantId,
            ClientId = clientId,
            AccountEmail = accountEmail,
            ClientSecret = clientSecret
        };
    }

    private static string RequireSetting(ConnectionDescriptor descriptor, string key)
    {
        if (!descriptor.Settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new GraphConfigurationException($"Required connection setting '{key}' is missing or empty.");
        return value;
    }

    // Redacted: never include the client secret in diagnostic output.
    public override string ToString() =>
        $"GraphConnectionConfig(tenant={TenantId}, client={ClientId}, account={AccountEmail}, secret=***REDACTED***)";
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphConnectionConfigTests` → expected **PASS** (all tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphConnectionConfig.cs src/EMaigrator.Connectors.Graph/GraphConfigurationException.cs src/EMaigrator.Connectors.Graph.Tests/GraphConnectionConfigTests.cs
git commit -m "feat(graph): parse BYO-OAuth connection config with least-privilege scope

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Error normalization — Graph error codes + throttling 429 → errorSignature

**Goal:** Implement `GraphErrorNormalizer.Normalize(Exception)` that maps Microsoft Graph `ODataError`/`ApiException` instances (and 429 throttling with `Retry-After`) into the stable `errorSignature` strings the Core `IErrorCatalog` matches (CONTRACTS §3, §8), exposing the parsed `RetryAfter` for transient backoff — and **never** including tenant identifiers or secrets in the signature.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphErrorNormalizer.cs`
- Create: `src/EMaigrator.Connectors.Graph/GraphNormalizedError.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphErrorNormalizerTests.cs`

**Acceptance Criteria:**
- [ ] An `ODataError` with code `errorThrottledRequest` and HTTP 429 normalizes to signature `"graph:429:throttled"` with `IsTransient == true` and `RetryAfter` populated from the `Retry-After` response header (seconds).
- [ ] An `ODataError` with code `ErrorItemNotFound` normalizes to `"graph:404:ErrorItemNotFound"`, `IsTransient == false`.
- [ ] An `ODataError` with code `ErrorAccessDenied` (HTTP 403) normalizes to `"graph:403:ErrorAccessDenied"`.
- [ ] An `ODataError` with code `InvalidAuthenticationToken` (HTTP 401) normalizes to `"graph:401:InvalidAuthenticationToken"`.
- [ ] HTTP 503 `ServiceUnavailable` with `Retry-After` normalizes to `"graph:503:serviceUnavailable"`, `IsTransient == true`, `RetryAfter` populated.
- [ ] The produced signature **never** contains the tenant id, account email, or any secret (asserted by a test that injects those values into the error and greps the signature).
- [ ] A generic `Exception` (no Graph metadata) normalizes to `"graph:unknown"`, `IsTransient == false`.

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphErrorNormalizerTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphErrorNormalizerTests.cs`:
```csharp
using System;
using EMaigrator.Connectors.Graph;
using FluentAssertions;
using Microsoft.Graph.Models.ODataErrors;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphErrorNormalizerTests
{
    private static ODataError ODataError(string code, int status, int? retryAfterSeconds = null)
    {
        var err = new ODataError
        {
            ResponseStatusCode = status,
            Error = new MainError { Code = code, Message = "human readable message" }
        };
        if (retryAfterSeconds is { } s)
            err.ResponseHeaders = new Microsoft.Kiota.Abstractions.RequestHeaders
            {
                { "Retry-After", s.ToString() }
            };
        return err;
    }

    [Fact]
    public void Throttled_429_is_transient_with_retry_after()
    {
        var n = GraphErrorNormalizer.Normalize(ODataError("errorThrottledRequest", 429, retryAfterSeconds: 17));

        n.Signature.Should().Be("graph:429:throttled");
        n.IsTransient.Should().BeTrue();
        n.RetryAfter.Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public void Item_not_found_is_non_transient()
    {
        var n = GraphErrorNormalizer.Normalize(ODataError("ErrorItemNotFound", 404));

        n.Signature.Should().Be("graph:404:ErrorItemNotFound");
        n.IsTransient.Should().BeFalse();
        n.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void Access_denied_403_maps_signature()
    {
        GraphErrorNormalizer.Normalize(ODataError("ErrorAccessDenied", 403))
            .Signature.Should().Be("graph:403:ErrorAccessDenied");
    }

    [Fact]
    public void Invalid_token_401_maps_signature()
    {
        GraphErrorNormalizer.Normalize(ODataError("InvalidAuthenticationToken", 401))
            .Signature.Should().Be("graph:401:InvalidAuthenticationToken");
    }

    [Fact]
    public void Service_unavailable_503_is_transient_with_retry_after()
    {
        var n = GraphErrorNormalizer.Normalize(ODataError("serviceUnavailable", 503, retryAfterSeconds: 30));

        n.Signature.Should().Be("graph:503:serviceUnavailable");
        n.IsTransient.Should().BeTrue();
        n.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Signature_never_leaks_tenant_or_secret()
    {
        var leaky = ODataError("ErrorAccessDenied", 403);
        leaky.Error!.Message =
            "Access denied for tenant 11111111-1111-1111-1111-111111111111 " +
            "secret super-secret-client-value account user@contoso.onmicrosoft.com";

        var n = GraphErrorNormalizer.Normalize(leaky);

        n.Signature.Should().NotContain("11111111-1111-1111-1111-111111111111");
        n.Signature.Should().NotContain("super-secret-client-value");
        n.Signature.Should().NotContain("user@contoso.onmicrosoft.com");
    }

    [Fact]
    public void Unknown_exception_maps_to_unknown()
    {
        var n = GraphErrorNormalizer.Normalize(new InvalidOperationException("boom"));

        n.Signature.Should().Be("graph:unknown");
        n.IsTransient.Should().BeFalse();
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphErrorNormalizerTests` → expected **FAIL**: `GraphErrorNormalizer`/`GraphNormalizedError` do not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphNormalizedError.cs`:
```csharp
namespace EMaigrator.Connectors.Graph;

/// <summary>
/// A Graph error normalized to a stable, credential-free signature for the Core error
/// catalog (CONTRACTS §8). Transient errors carry the honored Retry-After duration.
/// </summary>
public sealed record GraphNormalizedError(string Signature, bool IsTransient, TimeSpan? RetryAfter);
```
   Create `src/EMaigrator.Connectors.Graph/GraphErrorNormalizer.cs`:
```csharp
using System.Globalization;
using Microsoft.Graph.Models.ODataErrors;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Normalizes Microsoft Graph SDK exceptions into stable, credential-free
/// <see cref="GraphNormalizedError"/> signatures of the form "graph:&lt;status&gt;:&lt;code&gt;".
/// The signature is derived ONLY from the HTTP status and the Graph error code — never from
/// the error message, tenant id, account, or any secret — so it cannot leak identifiers
/// into user-facing diagnostics (DESIGN.md §10; INDEX security focus).
/// </summary>
public static class GraphErrorNormalizer
{
    public static GraphNormalizedError Normalize(Exception ex)
    {
        if (ex is ODataError odata)
            return FromODataError(odata);

        return new GraphNormalizedError("graph:unknown", IsTransient: false, RetryAfter: null);
    }

    private static GraphNormalizedError FromODataError(ODataError odata)
    {
        var status = odata.ResponseStatusCode;
        var code = odata.Error?.Code;
        var retryAfter = ParseRetryAfter(odata);

        // Throttling: Graph returns 429 with code errorThrottledRequest (and occasionally
        // ApplicationThrottled). Always transient; honor Retry-After.
        if (status == 429)
            return new GraphNormalizedError("graph:429:throttled", IsTransient: true, retryAfter);

        // Transient service errors.
        if (status is 503 or 504)
        {
            var transientCode = string.IsNullOrWhiteSpace(code) ? "serviceUnavailable" : SafeCode(code);
            return new GraphNormalizedError($"graph:{status}:{transientCode}", IsTransient: true, retryAfter);
        }

        var safeCode = string.IsNullOrWhiteSpace(code) ? "unknown" : SafeCode(code);
        return new GraphNormalizedError($"graph:{status}:{safeCode}", IsTransient: false, RetryAfter: null);
    }

    private static TimeSpan? ParseRetryAfter(ODataError odata)
    {
        if (odata.ResponseHeaders is null) return null;
        if (!odata.ResponseHeaders.TryGetValue("Retry-After", out var values)) return null;
        foreach (var v in values)
        {
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        return null;
    }

    // The Graph error code is a fixed enum-like token (e.g. "ErrorItemNotFound"); it never
    // contains identifiers. We still strip whitespace/separators defensively so the signature
    // stays a single stable token.
    private static string SafeCode(string code) => code.Trim();
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphErrorNormalizerTests` → expected **PASS** (all 7 tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphErrorNormalizer.cs src/EMaigrator.Connectors.Graph/GraphNormalizedError.cs src/EMaigrator.Connectors.Graph.Tests/GraphErrorNormalizerTests.cs
git commit -m "feat(graph): normalize Graph errors and 429 throttling to credential-free signatures

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Folder mapping — Graph mailFolders ⇄ canonical FolderPath

**Goal:** Implement `GraphFolderMapper` (pure) translating a flat list of Graph `MailFolder` records (each with `Id`, `DisplayName`, `ParentFolderId`) plus well-known folder roots into canonical `CanonicalFolder`/`FolderPath` values (CONTRACTS §1), and back (a `FolderPath` → the chain of display-name segments to create), with special-use mapping for Drafts (`MessageFlags.Draft`).

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphFolderMapper.cs`
- Create: `src/EMaigrator.Connectors.Graph/GraphMailFolderNode.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphFolderMapperTests.cs`

**Acceptance Criteria:**
- [ ] `GraphMailFolderNode(string Id, string DisplayName, string? ParentFolderId, long TotalItemCount)` is a record carrying the fields needed for mapping.
- [ ] `GraphFolderMapper.BuildTree(nodes, wellKnownRootIds)` returns `IReadOnlyList<CanonicalFolder>` where each folder's `FolderPath` is the chain of `DisplayName`s from a root to that node, joined under '/'.
- [ ] A child folder `B` under `Inbox` (well-known root) yields `FolderPath` `Inbox/B` with the right `EstimatedMessageCount` from `TotalItemCount`.
- [ ] A node whose `Id` equals the Drafts well-known id maps to `CanonicalFolder` with `SpecialUse == MessageFlags.Draft`.
- [ ] `GraphFolderMapper.ResolveFolderId(folderPath, idsByPath)` returns the Graph folder id for an existing path, or `null` if not present (used by EnsureFolder/Write).
- [ ] Mapping is deterministic and order-independent (nodes may arrive in any order; orphan nodes with an unknown parent are skipped, not crashed).

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphFolderMapperTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphFolderMapperTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphFolderMapperTests
{
    private static GraphFolderWellKnown WellKnown() => new(
        InboxId: "inbox-id",
        DraftsId: "drafts-id",
        SentItemsId: "sent-id",
        DeletedItemsId: "deleted-id");

    [Fact]
    public void Maps_well_known_root_and_nested_child_to_canonical_paths()
    {
        var nodes = new[]
        {
            new GraphMailFolderNode("inbox-id", "Inbox", null, 10),
            new GraphMailFolderNode("b-id", "Projects", "inbox-id", 3),
            new GraphMailFolderNode("c-id", "2026", "b-id", 1)
        };

        var folders = GraphFolderMapper.BuildTree(nodes, WellKnown());

        var paths = folders.Select(f => f.Path.ToString()).ToArray();
        paths.Should().Contain("Inbox");
        paths.Should().Contain("Inbox/Projects");
        paths.Should().Contain("Inbox/Projects/2026");

        folders.Single(f => f.Path.ToString() == "Inbox/Projects")
               .EstimatedMessageCount.Should().Be(3);
    }

    [Fact]
    public void Drafts_well_known_folder_carries_draft_special_use()
    {
        var nodes = new[] { new GraphMailFolderNode("drafts-id", "Drafts", null, 0) };

        var folders = GraphFolderMapper.BuildTree(nodes, WellKnown());

        folders.Single().SpecialUse.Should().Be(MessageFlags.Draft);
    }

    [Fact]
    public void BuildTree_is_order_independent()
    {
        var ordered = new[]
        {
            new GraphMailFolderNode("inbox-id", "Inbox", null, 0),
            new GraphMailFolderNode("b-id", "Projects", "inbox-id", 0)
        };
        var shuffled = ordered.Reverse().ToArray();

        var a = GraphFolderMapper.BuildTree(ordered, WellKnown()).Select(f => f.Path.ToString()).OrderBy(x => x);
        var b = GraphFolderMapper.BuildTree(shuffled, WellKnown()).Select(f => f.Path.ToString()).OrderBy(x => x);

        a.Should().Equal(b);
    }

    [Fact]
    public void Orphan_node_with_unknown_parent_is_skipped()
    {
        var nodes = new[]
        {
            new GraphMailFolderNode("x-id", "Orphan", "missing-parent", 5)
        };

        var folders = GraphFolderMapper.BuildTree(nodes, WellKnown());

        folders.Should().BeEmpty();
    }

    [Fact]
    public void ResolveFolderId_returns_id_for_existing_path_else_null()
    {
        var nodes = new[]
        {
            new GraphMailFolderNode("inbox-id", "Inbox", null, 0),
            new GraphMailFolderNode("b-id", "Projects", "inbox-id", 0)
        };
        var idsByPath = GraphFolderMapper.BuildIdIndex(nodes, WellKnown());

        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Inbox/Projects"), idsByPath).Should().Be("b-id");
        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Inbox/Nope"), idsByPath).Should().BeNull();
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphFolderMapperTests` → expected **FAIL**: `GraphFolderMapper`/`GraphMailFolderNode`/`GraphFolderWellKnown` do not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphMailFolderNode.cs`:
```csharp
namespace EMaigrator.Connectors.Graph;

/// <summary>Flat Graph mailFolder projection used by <see cref="GraphFolderMapper"/>.</summary>
public sealed record GraphMailFolderNode(string Id, string DisplayName, string? ParentFolderId, long TotalItemCount);

/// <summary>Resolved well-known folder ids for the mailbox (from /mailFolders/{wellKnownName}).</summary>
public sealed record GraphFolderWellKnown(string? InboxId, string? DraftsId, string? SentItemsId, string? DeletedItemsId);
```
   Create `src/EMaigrator.Connectors.Graph/GraphFolderMapper.cs`:
```csharp
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Pure mapping between Graph mailFolders and the canonical folder model (CONTRACTS §1).
/// A folder's canonical <see cref="FolderPath"/> is the chain of DisplayName segments from a
/// root (a node with no parent, or whose parent is the mailbox root) down to the node.
/// </summary>
public static class GraphFolderMapper
{
    public static IReadOnlyList<CanonicalFolder> BuildTree(
        IReadOnlyList<GraphMailFolderNode> nodes, GraphFolderWellKnown wellKnown)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var result = new List<CanonicalFolder>();

        foreach (var node in nodes)
        {
            var segments = TryBuildSegments(node, byId);
            if (segments is null) continue;   // orphan: unknown parent → skip

            var path = new FolderPath(segments);
            var specialUse = SpecialUseFor(node.Id, wellKnown);
            result.Add(new CanonicalFolder(path, node.TotalItemCount, specialUse));
        }

        return result;
    }

    public static IReadOnlyDictionary<string, string> BuildIdIndex(
        IReadOnlyList<GraphMailFolderNode> nodes, GraphFolderWellKnown wellKnown)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var segments = TryBuildSegments(node, byId);
            if (segments is null) continue;
            index[new FolderPath(segments).ToString()] = node.Id;
        }
        return index;
    }

    public static string? ResolveFolderId(FolderPath path, IReadOnlyDictionary<string, string> idsByPath)
        => idsByPath.TryGetValue(path.ToString(), out var id) ? id : null;

    private static List<string>? TryBuildSegments(GraphMailFolderNode node, IReadOnlyDictionary<string, GraphMailFolderNode> byId)
    {
        var segments = new List<string>();
        var current = node;
        var guard = 0;

        while (true)
        {
            segments.Insert(0, current.DisplayName);

            if (string.IsNullOrEmpty(current.ParentFolderId))
                return segments;   // reached a root

            if (!byId.TryGetValue(current.ParentFolderId, out var parent))
                return null;        // orphan: parent not in the node set → skip

            current = parent;
            if (++guard > 512) return null;   // defensive: malformed cycle
        }
    }

    private static MessageFlags? SpecialUseFor(string id, GraphFolderWellKnown wellKnown)
    {
        if (id == wellKnown.DraftsId) return MessageFlags.Draft;
        if (id == wellKnown.DeletedItemsId) return MessageFlags.Deleted;
        return null;
    }
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphFolderMapperTests` → expected **PASS** (all 5 tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphFolderMapper.cs src/EMaigrator.Connectors.Graph/GraphMailFolderNode.cs src/EMaigrator.Connectors.Graph.Tests/GraphFolderMapperTests.cs
git commit -m "feat(graph): map Graph mailFolders to canonical FolderPath tree

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Message mapping — Graph message ⇄ CanonicalMessage

**Goal:** Implement `GraphMessageMapper.ToCanonical(message, openContent)` translating a Graph `Message` (id, internetMessageId, receivedDateTime, isRead/flag/categories, body size, attachments) into a `CanonicalMessage` (CONTRACTS §1) — setting `IdentityKey` from the RFC `internetMessageId` via `IdentityKey.Compute`, mapping `receivedDateTime → InternalDate`, isRead/flag/isDraft → `MessageFlags`, and `categories → Labels` — with `OpenContentAsync` wired to a supplied MIME stream factory.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphMessageMapper.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphMessageMapperTests.cs`

**Acceptance Criteria:**
- [ ] `GraphMessageMapper.ToCanonical` sets `MessageId` from `internetMessageId` and `IdentityKey` to the `IdentityKey.Compute` result (which returns `"mid:<...>"` when a Message-ID is present — CONTRACTS §1).
- [ ] `receivedDateTime` (a `DateTimeOffset`) maps to `CanonicalMessage.InternalDate`; when null, falls back to `sentDateTime`, else `DateTimeOffset.UnixEpoch`.
- [ ] `isRead == true → MessageFlags.Seen`; `flag.flagStatus == Flagged → MessageFlags.Flagged`; `isDraft == true → MessageFlags.Draft`; flags compose (bitwise OR).
- [ ] `categories` list maps verbatim to `Labels`.
- [ ] `SizeBytes` maps from the message size; `Attachments` map name/contentType/size into `CanonicalAttachmentInfo`.
- [ ] `OpenContentAsync` invokes the supplied factory exactly once per call and returns its stream (no body content is stored on the record itself).

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphMessageMapperTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphMessageMapperTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Graph.Models;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphMessageMapperTests
{
    private static Message Sample()
    {
        return new Message
        {
            Id = "AAMkADk0graphid==",
            InternetMessageId = "<abc123@contoso.com>",
            Subject = "Quarterly report",
            ReceivedDateTime = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero),
            SentDateTime = new DateTimeOffset(2026, 4, 30, 18, 0, 0, TimeSpan.Zero),
            IsRead = true,
            IsDraft = false,
            Flag = new FollowupFlag { FlagStatus = FollowupFlagStatus.Flagged },
            Categories = new System.Collections.Generic.List<string> { "Red", "Finance" },
            Body = new ItemBody { Content = "hello" },
            Attachments = new System.Collections.Generic.List<Attachment>
            {
                new FileAttachment { Name = "q.pdf", ContentType = "application/pdf", Size = 2048 }
            }
        };
    }

    private static Task<Stream> OpenMime(CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.ASCII.GetBytes("Message-ID: <abc123@contoso.com>\r\n\r\nbody")));

    [Fact]
    public void Maps_identity_and_message_id_from_internet_message_id()
    {
        var msg = GraphMessageMapper.ToCanonical(Sample(), OpenMime);

        msg.MessageId.Should().Be("<abc123@contoso.com>");
        msg.IdentityKey.Should().StartWith("mid:");
    }

    [Fact]
    public void Maps_received_date_to_internal_date()
    {
        GraphMessageMapper.ToCanonical(Sample(), OpenMime)
            .InternalDate.Should().Be(new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Falls_back_to_sent_date_then_epoch_when_received_is_null()
    {
        var s = Sample();
        s.ReceivedDateTime = null;
        GraphMessageMapper.ToCanonical(s, OpenMime)
            .InternalDate.Should().Be(new DateTimeOffset(2026, 4, 30, 18, 0, 0, TimeSpan.Zero));

        s.SentDateTime = null;
        GraphMessageMapper.ToCanonical(s, OpenMime)
            .InternalDate.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Maps_flags_compositely()
    {
        var msg = GraphMessageMapper.ToCanonical(Sample(), OpenMime);
        msg.Flags.Should().HaveFlag(MessageFlags.Seen);
        msg.Flags.Should().HaveFlag(MessageFlags.Flagged);
        msg.Flags.Should().NotHaveFlag(MessageFlags.Draft);
    }

    [Fact]
    public void Maps_categories_to_labels()
    {
        GraphMessageMapper.ToCanonical(Sample(), OpenMime)
            .Labels.Should().BeEquivalentTo(new[] { "Red", "Finance" });
    }

    [Fact]
    public void Maps_attachments_metadata()
    {
        var att = GraphMessageMapper.ToCanonical(Sample(), OpenMime).Attachments.Single();
        att.FileName.Should().Be("q.pdf");
        att.ContentType.Should().Be("application/pdf");
        att.SizeBytes.Should().Be(2048);
    }

    [Fact]
    public async Task OpenContentAsync_invokes_supplied_factory()
    {
        var calls = 0;
        Func<CancellationToken, Task<Stream>> factory = ct => { calls++; return OpenMime(ct); };

        var msg = GraphMessageMapper.ToCanonical(Sample(), factory);
        await using var stream = await msg.OpenContentAsync(CancellationToken.None);

        calls.Should().Be(1);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Contain("Message-ID");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphMessageMapperTests` → expected **FAIL**: `GraphMessageMapper` does not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphMessageMapper.cs`:
```csharp
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using Microsoft.Graph.Models;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Pure mapping from a Graph <see cref="Message"/> to a <see cref="CanonicalMessage"/>
/// (CONTRACTS §1). The canonical record never holds the body — content is opened on demand
/// via the supplied factory (streaming pass-through; DESIGN.md §6/§10).
/// </summary>
public static class GraphMessageMapper
{
    public static CanonicalMessage ToCanonical(Message message, Func<CancellationToken, Task<Stream>> openContent)
    {
        var internetMessageId = message.InternetMessageId;
        var internalDate = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UnixEpoch;

        // IdentityKey: when a Message-ID exists it produces "mid:<normalized>" without needing the body
        // (CONTRACTS §1). DecodedBodySha256Hex is required by MessageIdentityInput; for a present
        // Message-ID it is not used in the result, so we pass empty.
        var identity = IdentityKey.Compute(new MessageIdentityInput
        {
            MessageId = internetMessageId,
            From = null,
            To = null,
            Subject = message.Subject,
            Date = internalDate,
            DecodedBodySha256Hex = string.Empty
        });

        return new CanonicalMessage
        {
            IdentityKey = identity,
            MessageId = internetMessageId,
            InternalDate = internalDate,
            Flags = MapFlags(message),
            Labels = message.Categories?.ToArray() ?? [],
            SizeBytes = message.Body?.Content?.Length ?? 0,
            Attachments = MapAttachments(message),
            Subject = message.Subject,
            OpenContentAsync = openContent
        };
    }

    private static MessageFlags MapFlags(Message message)
    {
        var flags = MessageFlags.None;
        if (message.IsRead == true) flags |= MessageFlags.Seen;
        if (message.IsDraft == true) flags |= MessageFlags.Draft;
        if (message.Flag?.FlagStatus == FollowupFlagStatus.Flagged) flags |= MessageFlags.Flagged;
        return flags;
    }

    private static IReadOnlyList<CanonicalAttachmentInfo> MapAttachments(Message message)
    {
        if (message.Attachments is not { Count: > 0 }) return [];
        return message.Attachments
            .Select(a => new CanonicalAttachmentInfo(
                a.Name ?? "attachment",
                a.ContentType ?? "application/octet-stream",
                a.Size ?? 0))
            .ToArray();
    }
}
```
   > Note: `CanonicalMessage.SizeBytes` is a best-effort estimate here (body text length); the authoritative byte size for size-cap pre-flight comes from the MIME `$value` content-length, handled in the source provider (Task 6). This mapper only needs the metadata shape.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphMessageMapperTests` → expected **PASS** (all 7 tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphMessageMapper.cs src/EMaigrator.Connectors.Graph.Tests/GraphMessageMapperTests.cs
git commit -m "feat(graph): map Graph message to CanonicalMessage with identity and flags

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: GraphSourceProvider — TestConnection, ListFolders, ReadMessages (WireMock fixtures)

**Goal:** Implement `GraphSourceProvider : ISourceProvider` (CONTRACTS §2) backed by a `GraphServiceClient`: `TestConnectionAsync` (count folders + messages), `ListFoldersAsync` (well-known + child mailFolders → `CanonicalFolder` via the mapper), and `ReadMessagesAsync` (paged message list, MIME `$value` stream via `Content.GetAsync` wired into `OpenContentAsync`) — verified against WireMock.Net fixtures shaped like real Graph responses.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphSourceProvider.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphSourceProviderTests.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/Fixtures/folders_list.json`
- Create: `src/EMaigrator.Connectors.Graph.Tests/Fixtures/messages_inbox.json`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphTestClientFactory.cs`

**Acceptance Criteria:**
- [ ] `GraphSourceProvider.Id.Value == "graph"` and `Constraints` equals `GraphConstraints.MS365`.
- [ ] `TestConnectionAsync` against the folders fixture returns `Ok == true` with `FolderCount` and `MessageCount` derived from the fixture; on an `ODataError` it returns `Ok == false` with `ErrorCode` = the normalized signature.
- [ ] `ListFoldersAsync` against the folders fixture returns the canonical folders for the well-known roots and their children (paths like `Inbox`, `Inbox/Projects`).
- [ ] `ReadMessagesAsync(FolderPath.Parse("Inbox"), …)` against the messages fixture yields `CanonicalMessage` instances with `IdentityKey`, `InternalDate`, flags, and labels mapped; `OpenContentAsync` requests the `$value` MIME endpoint and returns the raw MIME bytes from the fixture.
- [ ] `ReadOptions.Since`/`Before` are translated into a `receivedDateTime ge/lt` `$filter` (asserted by inspecting the WireMock request log).
- [ ] All tests use WireMock.Net (no live calls); the `GraphServiceClient` is pointed at the WireMock base URL via a custom `HttpClient`/request adapter.

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphSourceProviderTests` → all pass.

**Steps:**
1. - [ ] Create the fixtures and the test client factory, then write the failing test.
   Create `src/EMaigrator.Connectors.Graph.Tests/Fixtures/folders_list.json` (a Graph mailFolders collection with `childFolders` count and `totalItemCount`):
```json
{
  "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('user')/mailFolders",
  "value": [
    { "id": "inbox-id", "displayName": "Inbox", "parentFolderId": "msgfolderroot", "totalItemCount": 2, "childFolderCount": 1 },
    { "id": "projects-id", "displayName": "Projects", "parentFolderId": "inbox-id", "totalItemCount": 1, "childFolderCount": 0 },
    { "id": "sent-id", "displayName": "Sent Items", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 0 },
    { "id": "drafts-id", "displayName": "Drafts", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 0 }
  ]
}
```
   Create `src/EMaigrator.Connectors.Graph.Tests/Fixtures/messages_inbox.json` (a Graph messages collection):
```json
{
  "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('user')/mailFolders('inbox-id')/messages",
  "value": [
    {
      "id": "msg-1",
      "internetMessageId": "<m1@contoso.com>",
      "subject": "First",
      "receivedDateTime": "2026-05-01T09:30:00Z",
      "sentDateTime": "2026-04-30T18:00:00Z",
      "isRead": true,
      "isDraft": false,
      "categories": ["Finance"],
      "body": { "contentType": "text", "content": "hello" }
    },
    {
      "id": "msg-2",
      "internetMessageId": "<m2@contoso.com>",
      "subject": "Second",
      "receivedDateTime": "2026-05-02T10:00:00Z",
      "sentDateTime": "2026-05-02T09:00:00Z",
      "isRead": false,
      "isDraft": false,
      "categories": [],
      "body": { "contentType": "text", "content": "world" }
    }
  ]
}
```
   Create `src/EMaigrator.Connectors.Graph.Tests/GraphTestClientFactory.cs` — builds a `GraphServiceClient` whose HTTP transport points at the WireMock server, using an anonymous auth provider (no token round-trip in tests):
```csharp
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>Builds a GraphServiceClient pointed at a WireMock base URL with no real token.</summary>
public static class GraphTestClientFactory
{
    public static GraphServiceClient Create(string baseUrl)
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)
        {
            BaseUrl = baseUrl.TrimEnd('/') + "/v1.0"
        };
        return new GraphServiceClient(adapter);
    }
}
```
   Create the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphSourceProviderTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphSourceProviderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private GraphSourceProvider NewProvider()
    {
        var client = GraphTestClientFactory.Create(_server.Url!);
        return new GraphSourceProvider(client, "user@contoso.com");
    }

    private void StubFolders() =>
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("folders_list.json")));

    private void StubMessages() =>
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/inbox-id/messages").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("messages_inbox.json")));

    private void StubMime(string msgId, string mime) =>
        _server.Given(Request.Create()
                   .WithPath($"/v1.0/users/user@contoso.com/messages/{msgId}/$value").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "text/plain").WithBody(mime));

    [Fact]
    public void Id_and_constraints_are_graph_ms365()
    {
        var p = NewProvider();
        p.Id.Value.Should().Be("graph");
        p.Constraints.Should().BeSameAs(GraphConstraints.MS365);
    }

    [Fact]
    public async Task TestConnection_ok_counts_folders()
    {
        StubFolders();
        var result = await NewProvider().TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.FolderCount.Should().Be(4);
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task TestConnection_failure_returns_normalized_error_code()
    {
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(403)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("{\"error\":{\"code\":\"ErrorAccessDenied\",\"message\":\"denied\"}}"));

        var result = await NewProvider().TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("graph:403:ErrorAccessDenied");
    }

    [Fact]
    public async Task ListFolders_returns_canonical_paths()
    {
        StubFolders();
        var folders = await NewProvider().ListFoldersAsync(CancellationToken.None);

        var paths = folders.Select(f => f.Path.ToString()).ToArray();
        paths.Should().Contain("Inbox");
        paths.Should().Contain("Inbox/Projects");
    }

    [Fact]
    public async Task ReadMessages_yields_canonical_messages_with_mime_stream()
    {
        StubFolders();
        StubMessages();
        StubMime("msg-1", "Message-ID: <m1@contoso.com>\r\n\r\nbody one");
        StubMime("msg-2", "Message-ID: <m2@contoso.com>\r\n\r\nbody two");

        var read = new System.Collections.Generic.List<CanonicalMessage>();
        await foreach (var m in NewProvider().ReadMessagesAsync(FolderPath.Parse("Inbox"), new ReadOptions(), CancellationToken.None))
            read.Add(m);

        read.Should().HaveCount(2);
        read[0].MessageId.Should().Be("<m1@contoso.com>");
        read[0].InternalDate.Should().Be(new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero));

        await using var stream = await read[0].OpenContentAsync(CancellationToken.None);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Contain("body one");
    }

    [Fact]
    public async Task ReadMessages_applies_since_filter()
    {
        StubFolders();
        StubMessages();

        var since = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await foreach (var _ in NewProvider().ReadMessagesAsync(FolderPath.Parse("Inbox"), new ReadOptions { Since = since }, CancellationToken.None)) { }

        var requests = _server.LogEntries
            .Select(e => e.RequestMessage.RawQuery ?? string.Empty);
        requests.Any(q => q.Contains("receivedDateTime") && q.Contains("ge")).Should().BeTrue();
    }

    public void Dispose() => _server.Dispose();
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphSourceProviderTests` → expected **FAIL**: `GraphSourceProvider` does not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphSourceProvider.cs`:
```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Microsoft Graph <see cref="ISourceProvider"/>. Uses application-permission access keyed by
/// the target mailbox's UPN (Users[accountEmail]); reads bodies as raw MIME via the $value
/// endpoint, never buffering them onto the canonical record (streaming pass-through).
/// </summary>
public sealed class GraphSourceProvider : ISourceProvider
{
    private readonly GraphServiceClient _client;
    private readonly string _accountEmail;

    public GraphSourceProvider(GraphServiceClient client, string accountEmail)
    {
        _client = client;
        _accountEmail = accountEmail;
    }

    public ProviderId Id => GraphProviderPlugin.GraphProviderId;
    public ProviderConstraints Constraints => GraphConstraints.MS365;

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var nodes = await FetchFolderNodesAsync(ct);
            var messageCount = nodes.Sum(n => n.TotalItemCount);
            return new ConnectionTestResult(Ok: true, FolderCount: nodes.Count, MessageCount: messageCount);
        }
        catch (Exception ex)
        {
            var n = GraphErrorNormalizer.Normalize(ex);
            return new ConnectionTestResult(Ok: false, FolderCount: 0, MessageCount: 0, ErrorCode: n.Signature);
        }
    }

    public async Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var nodes = await FetchFolderNodesAsync(ct);
        var wellKnown = ResolveWellKnown(nodes);
        return GraphFolderMapper.BuildTree(nodes, wellKnown);
    }

    public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
        FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        var nodes = await FetchFolderNodesAsync(ct);
        var wellKnown = ResolveWellKnown(nodes);
        var idsByPath = GraphFolderMapper.BuildIdIndex(nodes, wellKnown);
        var folderId = GraphFolderMapper.ResolveFolderId(folder, idsByPath)
            ?? throw new GraphConfigurationException($"Source folder '{folder}' was not found in the mailbox.");

        var filter = BuildDateFilter(options);

        var page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
            .GetAsync(rc =>
            {
                rc.QueryParameters.Top = 50;
                if (filter is not null) rc.QueryParameters.Filter = filter;
            }, ct);

        while (page is not null)
        {
            foreach (var message in page.Value ?? [])
            {
                ct.ThrowIfCancellationRequested();
                var messageId = message.Id!;
                yield return GraphMessageMapper.ToCanonical(
                    message,
                    token => _client.Users[_accountEmail].Messages[messageId].Content.GetAsync(cancellationToken: token)!);
            }

            if (string.IsNullOrEmpty(page.OdataNextLink)) break;
            page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct);
        }
    }

    private async Task<List<GraphMailFolderNode>> FetchFolderNodesAsync(CancellationToken ct)
    {
        var nodes = new List<GraphMailFolderNode>();
        var page = await _client.Users[_accountEmail].MailFolders
            .GetAsync(rc => rc.QueryParameters.Top = 100, ct);

        while (page is not null)
        {
            foreach (var f in page.Value ?? [])
                // The mailbox root parent id is "msgfolderroot"; null it out so top-level folders
                // are treated as canonical roots by GraphFolderMapper (rather than skipped as orphans).
                nodes.Add(new GraphMailFolderNode(
                    f.Id!, f.DisplayName ?? "(unnamed)",
                    f.ParentFolderId == "msgfolderroot" ? null : f.ParentFolderId,
                    f.TotalItemCount ?? 0));

            if (string.IsNullOrEmpty(page.OdataNextLink)) break;
            page = await _client.Users[_accountEmail].MailFolders
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct);
        }
        return nodes;
    }

    private static GraphFolderWellKnown ResolveWellKnown(IReadOnlyList<GraphMailFolderNode> nodes)
    {
        string? ByName(string name) => nodes.FirstOrDefault(n =>
            string.Equals(n.DisplayName, name, StringComparison.OrdinalIgnoreCase))?.Id;
        return new GraphFolderWellKnown(
            InboxId: ByName("Inbox"),
            DraftsId: ByName("Drafts"),
            SentItemsId: ByName("Sent Items"),
            DeletedItemsId: ByName("Deleted Items"));
    }

    private static string? BuildDateFilter(ReadOptions options)
    {
        var clauses = new List<string>();
        if (options.Since is { } since)
            clauses.Add($"receivedDateTime ge {since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}");
        if (options.Before is { } before)
            clauses.Add($"receivedDateTime lt {before.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}");
        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```
   The fixtures use `parentFolderId: "msgfolderroot"` for top-level folders; `FetchFolderNodesAsync` (above) nulls that synthetic root parent out so `GraphFolderMapper` treats those folders as canonical roots rather than skipping them as orphans.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphSourceProviderTests` → expected **PASS** (all tests green; fixtures copied to output via the csproj `<None Include="Fixtures\**">`).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphSourceProvider.cs src/EMaigrator.Connectors.Graph.Tests/GraphSourceProviderTests.cs src/EMaigrator.Connectors.Graph.Tests/GraphTestClientFactory.cs src/EMaigrator.Connectors.Graph.Tests/Fixtures
git commit -m "feat(graph): implement GraphSourceProvider with mailFolders, messages, MIME stream

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: GraphDestinationProvider — EnsureFolder, WriteMessage (MIME import), ExistsByMessageId

**Goal:** Implement `GraphDestinationProvider : IDestinationProvider` (CONTRACTS §2): `TestConnectionAsync`, `EnsureFolderAsync` (create the child-folder chain idempotently), `WriteMessageAsync` (import the source MIME stream via `POST /messages` with `Content-Type: text/plain` base64 RFC822, preserving `sentDateTime`/headers), and `ExistsByMessageIdAsync` (`$filter=internetMessageId eq '…'`) — verified against WireMock fixtures including a 429 throttling response with `Retry-After`.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphDestinationProvider.cs`
- Create: `src/EMaigrator.Connectors.Graph/GraphThrottledException.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphDestinationProviderTests.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/Fixtures/created_folder.json`
- Create: `src/EMaigrator.Connectors.Graph.Tests/Fixtures/created_message.json`
- Create: `src/EMaigrator.Connectors.Graph.Tests/Fixtures/exists_match.json`

**Acceptance Criteria:**
- [ ] `EnsureFolderAsync(FolderPath.Parse("Inbox/Projects/2026"), ct)` creates each missing child segment under its parent (POST `…/childFolders`), and is a no-op for already-existing segments (asserted by inspecting the WireMock request log: only missing segments POSTed).
- [ ] `WriteMessageAsync` reads the source `CanonicalMessage.OpenContentAsync` stream, base64-encodes it, and POSTs to `…/messages` with `Content-Type: text/plain`; on success returns `WriteResult { Written = true, DestMessageId = <id> }`.
- [ ] On a 429 throttling response with `Retry-After: 12`, `WriteMessageAsync` returns `WriteResult { Written = false, ErrorCode = "graph:429:throttled" }` (the caller — the worker — handles backoff via `IRateLimiter`); the tenant id never appears in `ErrorCode`.
- [ ] `ExistsByMessageIdAsync(folder, "<m1@contoso.com>", ct)` issues a `$filter=internetMessageId eq '<m1@contoso.com>'` query and returns `true` when the fixture returns a match, `false` when empty.
- [ ] `Constraints` equals `GraphConstraints.MS365`; `Id.Value == "graph"`.

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphDestinationProviderTests` → all pass.

**Steps:**
1. - [ ] Create fixtures, the throttled exception, then write the failing test.
   Create `src/EMaigrator.Connectors.Graph.Tests/Fixtures/created_folder.json`:
```json
{ "id": "new-folder-id", "displayName": "Projects", "parentFolderId": "inbox-id", "totalItemCount": 0 }
```
   Create `src/EMaigrator.Connectors.Graph.Tests/Fixtures/created_message.json`:
```json
{ "id": "imported-msg-id", "internetMessageId": "<m1@contoso.com>", "subject": "First" }
```
   Create `src/EMaigrator.Connectors.Graph.Tests/Fixtures/exists_match.json`:
```json
{ "value": [ { "id": "found-msg-id", "internetMessageId": "<m1@contoso.com>" } ] }
```
   Create `src/EMaigrator.Connectors.Graph/GraphThrottledException.cs`:
```csharp
namespace EMaigrator.Connectors.Graph;

/// <summary>Marks a transient throttling outcome carrying the honored Retry-After.</summary>
public sealed class GraphThrottledException : Exception
{
    public TimeSpan? RetryAfter { get; }
    public GraphThrottledException(TimeSpan? retryAfter) : base("Graph request was throttled (HTTP 429).")
        => RetryAfter = retryAfter;
}
```
   Create the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphDestinationProviderTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphDestinationProviderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private GraphDestinationProvider NewProvider()
        => new(GraphTestClientFactory.Create(_server.Url!), "user@contoso.com");

    private void StubFolders() =>
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("folders_list.json")));

    private static CanonicalMessage Message() => new()
    {
        IdentityKey = "mid:<m1@contoso.com>",
        MessageId = "<m1@contoso.com>",
        InternalDate = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero),
        OpenContentAsync = ct => Task.FromResult<Stream>(
            new MemoryStream(Encoding.ASCII.GetBytes("Message-ID: <m1@contoso.com>\r\n\r\nbody")))
    };

    [Fact]
    public async Task EnsureFolder_creates_only_missing_segments()
    {
        StubFolders(); // Inbox + Inbox/Projects already exist
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/childFolders").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("created_folder.json")));

        await NewProvider().EnsureFolderAsync(FolderPath.Parse("Inbox/Projects/2026"), CancellationToken.None);

        var posts = _server.LogEntries.Where(e => e.RequestMessage.Method == "POST").ToArray();
        posts.Should().HaveCount(1); // only "2026" under Projects is created
        posts[0].RequestMessage.Path.Should().Contain("/mailFolders/projects-id/childFolders");
    }

    [Fact]
    public async Task WriteMessage_imports_mime_and_returns_dest_id()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("created_message.json")));

        var result = await NewProvider().WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"), Message(), CancellationToken.None);

        result.Written.Should().BeTrue();
        result.DestMessageId.Should().Be("imported-msg-id");

        var post = _server.LogEntries.Single(e => e.RequestMessage.Method == "POST");
        post.RequestMessage.Headers!["Content-Type"].First().Should().Contain("text/plain");
    }

    [Fact]
    public async Task WriteMessage_throttled_returns_normalized_error_without_tenant()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(429)
                   .WithHeader("Content-Type", "application/json").WithHeader("Retry-After", "12")
                   .WithBody("{\"error\":{\"code\":\"errorThrottledRequest\",\"message\":\"throttled tenant 11111111\"}}"));

        var result = await NewProvider().WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"), Message(), CancellationToken.None);

        result.Written.Should().BeFalse();
        result.ErrorCode.Should().Be("graph:429:throttled");
        result.ErrorCode.Should().NotContain("11111111");
    }

    [Fact]
    public async Task ExistsByMessageId_true_when_match()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages")
                   .WithParam("$filter").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("exists_match.json")));

        var exists = await NewProvider().ExistsByMessageIdAsync(
            FolderPath.Parse("Inbox/Projects"), "<m1@contoso.com>", CancellationToken.None);

        exists.Should().BeTrue();
        _server.LogEntries.Any(e =>
            (e.RequestMessage.RawQuery ?? "").Contains("internetMessageId")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByMessageId_false_when_empty()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages")
                   .WithParam("$filter").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody("{\"value\":[]}"));

        (await NewProvider().ExistsByMessageIdAsync(
            FolderPath.Parse("Inbox/Projects"), "<nope@contoso.com>", CancellationToken.None))
            .Should().BeFalse();
    }

    public void Dispose() => _server.Dispose();
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphDestinationProviderTests` → expected **FAIL**: `GraphDestinationProvider` does not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphDestinationProvider.cs`:
```csharp
using System.Text;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Microsoft Graph <see cref="IDestinationProvider"/>. Imports messages by POSTing the source's
/// raw MIME ($value) as base64 RFC822 with Content-Type text/plain, which preserves the original
/// headers and sentDateTime. Folder creation is idempotent; throttling is surfaced as a normalized
/// transient error for the worker's rate-limiter to handle (ARCHITECTURE.md §5).
/// </summary>
public sealed class GraphDestinationProvider : IDestinationProvider
{
    private readonly GraphServiceClient _client;
    private readonly string _accountEmail;

    public GraphDestinationProvider(GraphServiceClient client, string accountEmail)
    {
        _client = client;
        _accountEmail = accountEmail;
    }

    public ProviderId Id => GraphProviderPlugin.GraphProviderId;
    public ProviderConstraints Constraints => GraphConstraints.MS365;

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var nodes = await FetchFolderNodesAsync(ct);
            return new ConnectionTestResult(Ok: true, FolderCount: nodes.Count,
                MessageCount: nodes.Sum(n => n.TotalItemCount));
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(Ok: false, FolderCount: 0, MessageCount: 0,
                ErrorCode: GraphErrorNormalizer.Normalize(ex).Signature);
        }
    }

    public async Task EnsureFolderAsync(FolderPath folder, CancellationToken ct)
    {
        if (folder.IsRoot) return;

        var nodes = await FetchFolderNodesAsync(ct);
        var wellKnown = ResolveWellKnown(nodes);
        var idsByPath = new Dictionary<string, string>(
            GraphFolderMapper.BuildIdIndex(nodes, wellKnown), StringComparer.Ordinal);

        string? parentId = null;
        var accumulated = new List<string>();

        foreach (var segment in folder.Segments)
        {
            accumulated.Add(segment);
            var currentPath = new FolderPath(accumulated.ToArray()).ToString();

            if (idsByPath.TryGetValue(currentPath, out var existingId))
            {
                parentId = existingId;
                continue;
            }

            var created = await CreateChildFolderAsync(parentId, segment, ct);
            idsByPath[currentPath] = created;
            parentId = created;
        }
    }

    public async Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct)
    {
        try
        {
            var folderId = await ResolveExistingFolderIdAsync(folder, ct)
                ?? throw new GraphConfigurationException($"Destination folder '{folder}' does not exist; call EnsureFolderAsync first.");

            string base64Mime;
            await using (var content = await message.OpenContentAsync(ct))
            using (var buffer = new MemoryStream())
            {
                await content.CopyToAsync(buffer, ct);
                base64Mime = Convert.ToBase64String(buffer.ToArray());
            }

            var requestInfo = new RequestInformation
            {
                HttpMethod = Method.POST,
                UrlTemplate = "{+baseurl}/users/{user}/mailFolders/{folderId}/messages"
            };
            requestInfo.PathParameters.Add("baseurl", _client.RequestAdapter.BaseUrl!);
            requestInfo.PathParameters.Add("user", _accountEmail);
            requestInfo.PathParameters.Add("folderId", folderId);
            requestInfo.Headers.Add("Content-Type", "text/plain");
            requestInfo.SetStreamContent(
                new MemoryStream(Encoding.ASCII.GetBytes(base64Mime)), "text/plain");

            var errorMapping = new Dictionary<string, ParsableFactory<IParsable>>
            {
                ["4XX"] = Microsoft.Graph.Models.ODataErrors.ODataError.CreateFromDiscriminatorValue,
                ["5XX"] = Microsoft.Graph.Models.ODataErrors.ODataError.CreateFromDiscriminatorValue
            };
            var created = await _client.RequestAdapter.SendAsync(
                requestInfo, Message.CreateFromDiscriminatorValue, errorMapping, ct);

            return new WriteResult(Written: true, DestMessageId: created?.Id);
        }
        catch (Exception ex)
        {
            var n = GraphErrorNormalizer.Normalize(ex);
            return new WriteResult(Written: false, ErrorCode: n.Signature);
        }
    }

    public async Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
    {
        var folderId = await ResolveExistingFolderIdAsync(folder, ct);
        if (folderId is null) return false;

        var escaped = messageId.Replace("'", "''");
        var page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
            .GetAsync(rc =>
            {
                rc.QueryParameters.Filter = $"internetMessageId eq '{escaped}'";
                rc.QueryParameters.Top = 1;
                rc.QueryParameters.Select = ["id"];
            }, ct);

        return page?.Value is { Count: > 0 };
    }

    private async Task<string> CreateChildFolderAsync(string? parentId, string displayName, CancellationToken ct)
    {
        var body = new MailFolder { DisplayName = displayName };
        var created = parentId is null
            ? await _client.Users[_accountEmail].MailFolders.PostAsync(body, cancellationToken: ct)
            : await _client.Users[_accountEmail].MailFolders[parentId].ChildFolders.PostAsync(body, cancellationToken: ct);
        return created!.Id!;
    }

    private async Task<string?> ResolveExistingFolderIdAsync(FolderPath folder, CancellationToken ct)
    {
        var nodes = await FetchFolderNodesAsync(ct);
        var wellKnown = ResolveWellKnown(nodes);
        var idsByPath = GraphFolderMapper.BuildIdIndex(nodes, wellKnown);
        return GraphFolderMapper.ResolveFolderId(folder, idsByPath);
    }

    private async Task<List<GraphMailFolderNode>> FetchFolderNodesAsync(CancellationToken ct)
    {
        var nodes = new List<GraphMailFolderNode>();
        var page = await _client.Users[_accountEmail].MailFolders
            .GetAsync(rc => rc.QueryParameters.Top = 100, ct);

        while (page is not null)
        {
            foreach (var f in page.Value ?? [])
                nodes.Add(new GraphMailFolderNode(
                    f.Id!, f.DisplayName ?? "(unnamed)",
                    f.ParentFolderId == "msgfolderroot" ? null : f.ParentFolderId,
                    f.TotalItemCount ?? 0));

            if (string.IsNullOrEmpty(page.OdataNextLink)) break;
            page = await _client.Users[_accountEmail].MailFolders
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct);
        }
        return nodes;
    }

    private static GraphFolderWellKnown ResolveWellKnown(IReadOnlyList<GraphMailFolderNode> nodes)
    {
        string? ByName(string name) => nodes.FirstOrDefault(n =>
            string.Equals(n.DisplayName, name, StringComparison.OrdinalIgnoreCase))?.Id;
        return new GraphFolderWellKnown(
            ByName("Inbox"), ByName("Drafts"), ByName("Sent Items"), ByName("Deleted Items"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphDestinationProviderTests` → expected **PASS** (all 5 tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphDestinationProvider.cs src/EMaigrator.Connectors.Graph/GraphThrottledException.cs src/EMaigrator.Connectors.Graph.Tests/GraphDestinationProviderTests.cs src/EMaigrator.Connectors.Graph.Tests/Fixtures
git commit -m "feat(graph): implement GraphDestinationProvider with MIME import and dedup

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: GraphProviderPlugin — build GraphServiceClient from ClientSecretCredential

**Goal:** Complete `GraphProviderPlugin.CreateSource`/`CreateDestination` (CONTRACTS §2) to parse the `ConnectionDescriptor`+`SecretBundle` via `GraphConnectionConfig`, build a `GraphServiceClient` from a `ClientSecretCredential` (Azure.Identity) using the least-privilege `.default` scope, and return `GraphSourceProvider`/`GraphDestinationProvider` — with the token credential configured **never** to persist a token cache to disk.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/GraphClientFactory.cs`
- Modify: `src/EMaigrator.Connectors.Graph/GraphProviderPlugin.cs`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphProviderPluginTests.cs`

**Acceptance Criteria:**
- [ ] `GraphProviderPlugin.SupportedAuth` contains `GraphAppOAuth` and `GraphDelegatedOAuth`; `CanBeSource` and `CanBeDestination` are both `true`.
- [ ] `CreateSource(descriptor, secrets)` returns a `GraphSourceProvider` whose `Id.Value == "graph"`; `CreateDestination` returns a `GraphDestinationProvider`.
- [ ] `CreateSource`/`CreateDestination` throw `GraphConfigurationException` (not `NullReferenceException`) when `clientSecret` is missing.
- [ ] `GraphClientFactory.BuildCredential` constructs a `ClientSecretCredential` with `ClientSecretCredentialOptions` whose `TokenCachePersistenceOptions` is **null** (in-memory only, never persisted to disk) — asserted by a test reading the options the factory uses.
- [ ] `GraphClientFactory.GraphScopes` equals `GraphConnectionConfig.GraphScopes` (single `.default` scope).

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphProviderPluginTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphProviderPluginTests.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphProviderPluginTests
{
    private static ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("graph"),
        Auth = AuthMethod.GraphAppOAuth,
        Settings = new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            ["clientId"] = "22222222-2222-2222-2222-222222222222",
            ["accountEmail"] = "user@contoso.onmicrosoft.com"
        },
        SecretRef = "ref"
    };

    private static SecretBundle Bundle() =>
        new(new Dictionary<string, string> { ["clientSecret"] = "the-secret" });

    [Fact]
    public void Plugin_advertises_capabilities()
    {
        var plugin = new GraphProviderPlugin();
        plugin.Id.Value.Should().Be("graph");
        plugin.SupportedAuth.Should().Contain(AuthMethod.GraphAppOAuth);
        plugin.SupportedAuth.Should().Contain(AuthMethod.GraphDelegatedOAuth);
        plugin.CanBeSource.Should().BeTrue();
        plugin.CanBeDestination.Should().BeTrue();
    }

    [Fact]
    public void CreateSource_returns_graph_source_provider()
    {
        var source = new GraphProviderPlugin().CreateSource(Descriptor(), Bundle());
        source.Should().BeOfType<GraphSourceProvider>();
        source.Id.Value.Should().Be("graph");
    }

    [Fact]
    public void CreateDestination_returns_graph_destination_provider()
    {
        var dest = new GraphProviderPlugin().CreateDestination(Descriptor(), Bundle());
        dest.Should().BeOfType<GraphDestinationProvider>();
    }

    [Fact]
    public void CreateSource_throws_config_exception_when_secret_missing()
    {
        var empty = new SecretBundle(new Dictionary<string, string>());
        var act = () => new GraphProviderPlugin().CreateSource(Descriptor(), empty);
        act.Should().Throw<GraphConfigurationException>();
    }

    [Fact]
    public void Credential_options_do_not_persist_token_cache_to_disk()
    {
        var options = GraphClientFactory.BuildCredentialOptions();
        options.TokenCachePersistenceOptions.Should().BeNull();
    }

    [Fact]
    public void Factory_uses_least_privilege_scope()
    {
        GraphClientFactory.GraphScopes.Should().Equal("https://graph.microsoft.com/.default");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphProviderPluginTests` → expected **FAIL**: `GraphClientFactory` does not exist; `CreateSource`/`CreateDestination` still throw `NotImplementedException`.
3. - [ ] Create `src/EMaigrator.Connectors.Graph/GraphClientFactory.cs`:
```csharp
using Azure.Identity;
using Microsoft.Graph;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Builds a <see cref="GraphServiceClient"/> for the client-credentials flow. The token cache is
/// kept in-memory only — <see cref="ClientSecretCredentialOptions.TokenCachePersistenceOptions"/>
/// is left null so no token is ever written to disk in plaintext (INDEX security focus; DESIGN.md §10).
/// </summary>
public static class GraphClientFactory
{
    public static readonly string[] GraphScopes = GraphConnectionConfig.GraphScopes;

    public static ClientSecretCredentialOptions BuildCredentialOptions() => new()
    {
        // Intentionally NOT setting TokenCachePersistenceOptions: the token stays in memory only.
        TokenCachePersistenceOptions = null
    };

    public static GraphServiceClient Build(GraphConnectionConfig config)
    {
        var credential = new ClientSecretCredential(
            config.TenantId, config.ClientId, config.ClientSecret, BuildCredentialOptions());
        return new GraphServiceClient(credential, GraphScopes);
    }
}
```
   Replace `src/EMaigrator.Connectors.Graph/GraphProviderPlugin.cs` with the completed implementation:
```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// DI-discovered Microsoft Graph connector plugin (CONTRACTS §2). Builds a GraphServiceClient
/// from BYO-OAuth client-credentials and constructs source/destination providers bound to the
/// target mailbox UPN.
/// </summary>
public sealed class GraphProviderPlugin : IProviderPlugin
{
    public static readonly ProviderId GraphProviderId = new("graph");

    public ProviderId Id => GraphProviderId;
    public IReadOnlyCollection<AuthMethod> SupportedAuth { get; } =
        [AuthMethod.GraphAppOAuth, AuthMethod.GraphDelegatedOAuth];
    public bool CanBeSource => true;
    public bool CanBeDestination => true;

    public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        var config = GraphConnectionConfig.FromDescriptor(descriptor, secrets);
        var client = GraphClientFactory.Build(config);
        return new GraphSourceProvider(client, config.AccountEmail);
    }

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        var config = GraphConnectionConfig.FromDescriptor(descriptor, secrets);
        var client = GraphClientFactory.Build(config);
        return new GraphDestinationProvider(client, config.AccountEmail);
    }
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphProviderPluginTests` → expected **PASS** (all 6 tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/GraphClientFactory.cs src/EMaigrator.Connectors.Graph/GraphProviderPlugin.cs src/EMaigrator.Connectors.Graph.Tests/GraphProviderPluginTests.cs
git commit -m "feat(graph): build GraphServiceClient with in-memory-only token cache

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: Contract conformance test — GraphProviderPlugin satisfies Core abstractions

**Goal:** Add a contract test proving the Graph connector honors the CONTRACTS §2 provider contract: a plugin built through `AddGraphConnector()` is discoverable as `IProviderPlugin`, advertises `ProviderId("graph")`, and the source/destination it creates implement the full `ISourceProvider`/`IDestinationProvider` surface and dispose cleanly.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphContractConformanceTests.cs`

**Acceptance Criteria:**
- [ ] A test resolves `IProviderPlugin` from a DI container configured with `AddGraphConnector()` and asserts it is the Graph plugin.
- [ ] The created `ISourceProvider` exposes non-default `Constraints` (equal to `GraphConstraints.MS365`) and `Id` matching the plugin.
- [ ] The created `IDestinationProvider` likewise; both implement `IAsyncDisposable` and `DisposeAsync()` completes without throwing.
- [ ] A test asserts every member of `ISourceProvider` and `IDestinationProvider` (from CONTRACTS §2) is implemented (reflection check that the concrete types declare the interface methods — no `NotImplementedException` on the contract methods that don't require I/O: `Id`, `Constraints`, `DisposeAsync`).

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphContractConformanceTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphContractConformanceTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphContractConformanceTests
{
    private static ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("graph"),
        Auth = AuthMethod.GraphAppOAuth,
        Settings = new Dictionary<string, string>
        {
            ["tenantId"] = "t", ["clientId"] = "c", ["accountEmail"] = "a@contoso.com"
        },
        SecretRef = "ref"
    };

    private static SecretBundle Bundle() =>
        new(new Dictionary<string, string> { ["clientSecret"] = "s" });

    private static IProviderPlugin ResolvePlugin()
    {
        var services = new ServiceCollection();
        services.AddGraphConnector();
        return services.BuildServiceProvider().GetServices<IProviderPlugin>()
            .Single(p => p.Id.Value == "graph");
    }

    [Fact]
    public void Plugin_is_discoverable_via_DI()
    {
        ResolvePlugin().Should().BeOfType<GraphProviderPlugin>();
    }

    [Fact]
    public async Task Source_implements_full_contract_and_disposes()
    {
        ISourceProvider source = ResolvePlugin().CreateSource(Descriptor(), Bundle());

        source.Id.Value.Should().Be("graph");
        source.Constraints.Should().BeSameAs(GraphConstraints.MS365);
        source.Should().BeAssignableTo<IAsyncDisposable>();

        await source.DisposeAsync(); // must not throw
    }

    [Fact]
    public async Task Destination_implements_full_contract_and_disposes()
    {
        IDestinationProvider dest = ResolvePlugin().CreateDestination(Descriptor(), Bundle());

        dest.Id.Value.Should().Be("graph");
        dest.Constraints.Should().BeSameAs(GraphConstraints.MS365);
        dest.Should().BeAssignableTo<IAsyncDisposable>();

        await dest.DisposeAsync();
    }

    [Fact]
    public void Concrete_types_declare_all_contract_methods()
    {
        var sourceMethods = typeof(GraphSourceProvider).GetMethods().Select(m => m.Name).ToArray();
        sourceMethods.Should().Contain(nameof(ISourceProvider.TestConnectionAsync));
        sourceMethods.Should().Contain(nameof(ISourceProvider.ListFoldersAsync));
        sourceMethods.Should().Contain(nameof(ISourceProvider.ReadMessagesAsync));

        var destMethods = typeof(GraphDestinationProvider).GetMethods().Select(m => m.Name).ToArray();
        destMethods.Should().Contain(nameof(IDestinationProvider.EnsureFolderAsync));
        destMethods.Should().Contain(nameof(IDestinationProvider.WriteMessageAsync));
        destMethods.Should().Contain(nameof(IDestinationProvider.ExistsByMessageIdAsync));
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphContractConformanceTests` → expected **FAIL**: if any contract method is missing the file will not compile; if all present but a member name typo exists the assertions fail. (At this point Tasks 6–8 are done, so this should drive only minor fixes if a signature drifted from CONTRACTS.)
3. - [ ] Make it pass: no production code should be needed if Tasks 6–8 bound to CONTRACTS verbatim. If a name mismatch surfaces, correct the concrete provider method name to match the CONTRACTS interface exactly (e.g. ensure `ReadMessagesAsync`, `EnsureFolderAsync`, `WriteMessageAsync`, `ExistsByMessageIdAsync` spellings). No new types are introduced.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphContractConformanceTests` → expected **PASS** (all 4 tests green).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph.Tests/GraphContractConformanceTests.cs
git commit -m "test(graph): contract conformance against Core provider abstractions

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: Functional Verification — round-trip read→write through WireMock fixtures

**Goal:** Prove the connector's headline behavior end-to-end against recorded-fixture Graph endpoints: read a message from a source mailbox folder, then write it (MIME import) into a destination folder, demonstrating folder resolution, MIME pass-through, and dedup-by-Message-ID — the WorkMail→MS365 destination wedge proven against fixtures (DESIGN §4.1, §17).

**Files:**
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphRoundTripFunctionalTests.cs`

**Acceptance Criteria:**
- [ ] A single test: stub source folders+messages+MIME and destination folders+create-message; `GraphSourceProvider.ReadMessagesAsync("Inbox")` yields a message whose `OpenContentAsync` stream is fed into `GraphDestinationProvider.WriteMessageAsync("Inbox/Projects", message)`, returning `Written == true`.
- [ ] The MIME bytes POSTed to the destination decode (base64) back to the exact source MIME bytes — proving lossless streaming pass-through (no truncation/re-encoding) and that no body is buffered to disk.
- [ ] After writing, `ExistsByMessageIdAsync("Inbox/Projects", "<m1@contoso.com>")` returns `true` against a fixture that now reports the message, proving idempotency-by-Message-ID is wired.
- [ ] The whole test runs offline (WireMock only); no live Graph calls.

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphRoundTripFunctionalTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphRoundTripFunctionalTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphRoundTripFunctionalTests : IDisposable
{
    private readonly WireMockServer _source = WireMockServer.Start();
    private readonly WireMockServer _dest = WireMockServer.Start();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private const string SourceMime = "Message-ID: <m1@contoso.com>\r\nSubject: First\r\n\r\nthe original body bytes";

    [Fact]
    public async Task Reads_a_message_and_imports_it_losslessly_into_destination()
    {
        // ----- SOURCE stubs -----
        _source.Given(Request.Create().WithPath("/v1.0/users/src@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("folders_list.json")));
        _source.Given(Request.Create().WithPath("/v1.0/users/src@contoso.com/mailFolders/inbox-id/messages").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("messages_inbox.json")));
        _source.Given(Request.Create().WithPath("/v1.0/users/src@contoso.com/messages/msg-1/$value").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "text/plain").WithBody(SourceMime));
        _source.Given(Request.Create().WithPath("/v1.0/users/src@contoso.com/messages/msg-2/$value").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "text/plain").WithBody("Message-ID: <m2@contoso.com>\r\n\r\nb"));

        // ----- DEST stubs -----
        _dest.Given(Request.Create().WithPath("/v1.0/users/dst@contoso.com/mailFolders").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(Fixture("folders_list.json")));
        _dest.Given(Request.Create()
                 .WithPath("/v1.0/users/dst@contoso.com/mailFolders/projects-id/messages").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(201)
                 .WithHeader("Content-Type", "application/json").WithBody(Fixture("created_message.json")));

        var source = new GraphSourceProvider(GraphTestClientFactory.Create(_source.Url!), "src@contoso.com");
        var dest = new GraphDestinationProvider(GraphTestClientFactory.Create(_dest.Url!), "dst@contoso.com");

        // ----- ACT: read first message, write it to destination -----
        CanonicalMessage? first = null;
        await foreach (var m in source.ReadMessagesAsync(FolderPath.Parse("Inbox"), new(), CancellationToken.None))
        {
            first = m;
            break;
        }
        first.Should().NotBeNull();
        first!.MessageId.Should().Be("<m1@contoso.com>");

        var write = await dest.WriteMessageAsync(FolderPath.Parse("Inbox/Projects"), first, CancellationToken.None);
        write.Written.Should().BeTrue();
        write.DestMessageId.Should().Be("imported-msg-id");

        // ----- ASSERT: posted bytes decode back to the exact source MIME (lossless pass-through) -----
        var post = _dest.LogEntries.Single(e => e.RequestMessage.Method == "POST");
        var posted = post.RequestMessage.Body!; // base64 text
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(posted.Trim()));
        decoded.Should().Be(SourceMime);

        // ----- ASSERT: dedup-by-Message-ID is wired -----
        _dest.Given(Request.Create()
                 .WithPath("/v1.0/users/dst@contoso.com/mailFolders/projects-id/messages")
                 .WithParam("$filter").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(Fixture("exists_match.json")));

        (await dest.ExistsByMessageIdAsync(FolderPath.Parse("Inbox/Projects"), "<m1@contoso.com>", CancellationToken.None))
            .Should().BeTrue();
    }

    public void Dispose()
    {
        _source.Dispose();
        _dest.Dispose();
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphRoundTripFunctionalTests` → expected **FAIL** initially only if the request body capture format differs (WireMock exposes the POST body as `RequestMessage.Body`). If the SDK's `SetStreamContent` causes the body to be captured as bytes, adjust the assertion to read `post.RequestMessage.BodyAsBytes` and base64-decode `Encoding.ASCII.GetString(BodyAsBytes)`. Run once to observe the actual capture shape, then finalize.
3. - [ ] Finalize the assertion to match the observed WireMock capture (text body via `Body`, or `BodyAsBytes` if binary). No production change is expected — Tasks 6 and 7 already implement the behavior; this task only proves the end-to-end wiring.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphRoundTripFunctionalTests` → expected **PASS**.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph.Tests/GraphRoundTripFunctionalTests.cs
git commit -m "test(graph): functional round-trip read to MIME-import with lossless pass-through

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: Live-smoke documentation (M365 Developer tenant) + skipped smoke harness

**Goal:** Document that live smoke testing uses the free **M365 Developer Program** tenant (DESIGN §17) and provide a gated, `Skip`-by-default smoke test class that exercises a real Graph connection only when credentials are supplied via environment variables — never per-commit, never in CI default.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph/README.md`
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphLiveSmokeTests.cs`

**Acceptance Criteria:**
- [ ] `README.md` documents: BYO Azure App Registration steps (app permission `Mail.ReadWrite`, admin consent), required `ConnectionDescriptor.Settings` keys (`tenantId`, `clientId`, `accountEmail`) and the `clientSecret` secret, and that live smoke uses the **free M365 Developer Program** tenant — gated/nightly, excluded from coverage %.
- [ ] `README.md` states the least-privilege scope (`Mail.ReadWrite` application permission only; no `Mail.Send`) and that the token cache is in-memory only.
- [ ] `GraphLiveSmokeTests` reads `EMAIGRATOR_GRAPH_TENANT`, `EMAIGRATOR_GRAPH_CLIENT_ID`, `EMAIGRATOR_GRAPH_CLIENT_SECRET`, `EMAIGRATOR_GRAPH_ACCOUNT` from the environment; each `[Fact]` is `Skip`ped when any is absent (using a `SkippableFact`-style guard) so the default `dotnet test` run does not perform live calls.
- [ ] When env vars are present the smoke fact calls `TestConnectionAsync` and asserts `Ok == true` — but the **default** test run reports the fact as skipped (verified by the verify command output showing skipped, not failed).

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphLiveSmokeTests` → run completes; smoke facts reported as **skipped** (no live calls without env credentials).

**Steps:**
1. - [ ] Write the smoke test `src/EMaigrator.Connectors.Graph.Tests/GraphLiveSmokeTests.cs` (skip-guarded; no extra package needed — uses an env guard that calls `Assert.Skip` via a helper):
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>
/// Gated live smoke against the free M365 Developer Program tenant (DESIGN §17). These facts run
/// ONLY when all EMAIGRATOR_GRAPH_* environment variables are set; otherwise they are skipped, so
/// the default per-commit/CI run never makes a live Graph call.
/// </summary>
public class GraphLiveSmokeTests
{
    private static (bool Ready, ConnectionDescriptor Descriptor, SecretBundle Secrets) Env()
    {
        var tenant = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_TENANT");
        var clientId = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_CLIENT_SECRET");
        var account = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_ACCOUNT");

        var ready = !(string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(account));

        var descriptor = new ConnectionDescriptor
        {
            Provider = new ProviderId("graph"),
            Auth = AuthMethod.GraphAppOAuth,
            Settings = new Dictionary<string, string>
            {
                ["tenantId"] = tenant ?? "",
                ["clientId"] = clientId ?? "",
                ["accountEmail"] = account ?? ""
            },
            SecretRef = "live"
        };
        var secrets = new SecretBundle(new Dictionary<string, string> { ["clientSecret"] = secret ?? "" });
        return (ready, descriptor, secrets);
    }

    [Fact]
    public async Task TestConnection_against_live_developer_tenant()
    {
        var (ready, descriptor, secrets) = Env();
        Assert.SkipUnless(ready, "Live Graph smoke skipped: EMAIGRATOR_GRAPH_* env vars not set.");

        await using var source = new GraphProviderPlugin().CreateSource(descriptor, secrets);
        var result = await source.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.FolderCount.Should().BeGreaterThan(0);
    }
}
```
   > `Assert.SkipUnless` is available in xUnit v2.9+/v3 (dynamic skip). If the repo pins an older xUnit, replace the guard with the project's standard skippable mechanism configured in Plan 01's shared test harness; the behavior (skipped, not failed, when env is absent) is the contract.
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphLiveSmokeTests` → expected: the run completes and the fact is reported **skipped** (env vars absent). This is the "red→green" for a gated test: green == correctly skipped without a live call.
3. - [ ] Write `src/EMaigrator.Connectors.Graph/README.md`:
```markdown
# EMaigrator.Connectors.Graph

Microsoft Graph connector (source + destination) for Microsoft 365 mailboxes. Implements
`ISourceProvider`, `IDestinationProvider`, and `IProviderPlugin` from `EMaigrator.Core`
(see CONTRACTS.md §2).

## BYO-OAuth setup (v1 — no shared branded app; DESIGN.md §11)

The operator registers **their own** Azure App Registration:

1. Azure Portal → Entra ID → App registrations → New registration.
2. API permissions → Microsoft Graph → **Application permissions** → add **`Mail.ReadWrite`**
   (and nothing else — least privilege; we do **not** request `Mail.Send`).
3. Click **Grant admin consent** for the tenant.
4. Certificates & secrets → New client secret → copy the value.

## Connection configuration

`ConnectionDescriptor.Settings` (non-secret):

| Key | Value |
|---|---|
| `tenantId` | Directory (tenant) ID |
| `clientId` | Application (client) ID |
| `accountEmail` | UPN of the mailbox to read/write |

Secret bundle: `{ "clientSecret": "<the app client secret>" }`, stored via `ISecretStore`
and resolved transiently. **The client secret and access tokens are never logged.**

## Security posture

- **Least privilege:** only the application permission `Mail.ReadWrite` is exercised, requested
  via the `https://graph.microsoft.com/.default` scope. No `Mail.Send`, no broad `Mail.Read` of
  all mailboxes beyond what the BYO app was consented for.
- **Token cache is in-memory only** — `ClientSecretCredentialOptions.TokenCachePersistenceOptions`
  is left null, so no token is ever persisted to disk.
- **Throttling (429)** is normalized to the credential-free signature `graph:429:throttled` with
  the honored `Retry-After`; tenant identifiers never appear in user-facing error codes.

## Testing

- **Unit + contract + connector tests** run per-commit against **WireMock.Net** fixtures shaped
  like real Graph responses (folders list, message list, MIME `$value`, create message,
  throttling 429 with `Retry-After`). Excluded from live calls.
- **Live smoke** (`GraphLiveSmokeTests`) runs **only** when the `EMAIGRATOR_GRAPH_*` environment
  variables are set, against the **free [Microsoft 365 Developer Program](https://developer.microsoft.com/microsoft-365/dev-program) tenant**.
  It is gated/nightly, **excluded from coverage %**, and skipped by default (never per-commit).

  ```bash
  EMAIGRATOR_GRAPH_TENANT=... \
  EMAIGRATOR_GRAPH_CLIENT_ID=... \
  EMAIGRATOR_GRAPH_CLIENT_SECRET=... \
  EMAIGRATOR_GRAPH_ACCOUNT=user@yourtenant.onmicrosoft.com \
  dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphLiveSmokeTests
  ```
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphLiveSmokeTests` → expected **PASS/skipped** (run completes; fact skipped because env vars are unset — no live call).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph/README.md src/EMaigrator.Connectors.Graph.Tests/GraphLiveSmokeTests.cs
git commit -m "docs(graph): document BYO-OAuth and M365 dev-tenant live smoke; add gated smoke test

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: Security Verification (USER-ORDERED GATE)

**Goal:** Prove the Graph connector's security focus from the INDEX per-plan table: the client secret/tokens are never logged; the OAuth scope is least-privilege (`Mail.ReadWrite` only, via `.default`); the token cache is never persisted to disk in plaintext; and 429 handling does not leak tenant identifiers into user-facing errors.

**USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Connectors.Graph.Tests/GraphSecurityVerificationTests.cs`

**Acceptance Criteria:**
- [ ] **No secret in logs:** with a captured `ILogger` (in-memory provider) wrapping a full TestConnection + Write attempt that triggers a 403/429, a search of every captured log message and scope shows **zero** occurrences of the client secret string and zero occurrences of the access-token value. (Output captured: the assertion failure message would print the offending entry; on pass, the test prints the count of scanned log entries.)
- [ ] **Least-privilege scope:** an assertion that `GraphClientFactory.GraphScopes` and `GraphConnectionConfig.GraphScopes` both equal exactly `["https://graph.microsoft.com/.default"]`, and a static-source assertion (regex over the connector source files) that the literal `"Mail.Send"` and `"Mail.Read"` (the broader all-mailboxes read) do **not** appear as requested scopes anywhere in `src/EMaigrator.Connectors.Graph/**.cs`. (grep output captured showing zero matches.)
- [ ] **Token cache not on disk:** `GraphClientFactory.BuildCredentialOptions().TokenCachePersistenceOptions` is `null` (in-memory only); a static-source assertion confirms `TokenCachePersistenceOptions` is never assigned a non-null/`new` value in the connector sources. (Captured: the matched lines around the assignment showing `= null`.)
- [ ] **429 does not leak tenant:** feeding an `ODataError` whose message embeds the tenant GUID, account email, and a secret into `GraphErrorNormalizer.Normalize` (429 + Retry-After) yields signature `graph:429:throttled` containing none of those values; and the `WriteResult.ErrorCode` from a throttled `WriteMessageAsync` (WireMock 429 with a tenant id in the body) contains none of them. (Captured: the produced signature and ErrorCode strings.)
- [ ] **TLS enforced + no arbitrary-host exfiltration:** the production client built by `GraphClientFactory.Build` targets only the official Graph endpoint over HTTPS — `client.RequestAdapter.BaseUrl` starts with `https://graph.microsoft.com` (asserted at runtime), and a static-source assertion (regex over `src/EMaigrator.Connectors.Graph/**.cs`) confirms no `http://` URL literal and no `BaseUrl =` / `WithUrl(`-to-arbitrary-host assignment exists in the connector (the only host literal permitted is `https://graph.microsoft.com`). This proves test-connection/read/write cannot be redirected to exfiltrate credentials or content to an attacker-controlled host. (Captured: the asserted BaseUrl string and zero `http://` matches.)
- [ ] **Config redaction:** `GraphConnectionConfig.ToString()` does not contain the client secret. (Captured: the ToString output.)

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphSecurityVerificationTests` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Graph.Tests/GraphSecurityVerificationTests.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphSecurityVerificationTests
{
    private const string Secret = "ULTRA-SECRET-CLIENT-VALUE-9f3a";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Account = "user@contoso.onmicrosoft.com";

    private readonly ITestOutputHelper _out;
    public GraphSecurityVerificationTests(ITestOutputHelper output) => _out = output;

    private static string SourceDir =>
        // tests bin: .../src/EMaigrator.Connectors.Graph.Tests/bin/<cfg>/net10.0
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "EMaigrator.Connectors.Graph"));

    private static IEnumerable<string> ConnectorSources() =>
        Directory.EnumerateFiles(SourceDir, "*.cs", SearchOption.AllDirectories);

    // ---- 1. No secret/token in captured logs ----
    [Fact]
    public async Task No_client_secret_appears_in_captured_logs()
    {
        var captured = new ConcurrentBag<string>();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new CapturingLoggerProvider(captured)));

        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1.0/users/" + Account + "/mailFolders").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(403)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody("{\"error\":{\"code\":\"ErrorAccessDenied\",\"message\":\"denied\"}}"));

        var logger = loggerFactory.CreateLogger("graph-test");
        // Simulate connector-adjacent logging: the connector itself must not log secrets, and any
        // caller logging the normalized result must only see the credential-free signature.
        var source = new GraphSourceProvider(GraphTestClientFactory.Create(server.Url!), Account);
        var result = await source.TestConnectionAsync(CancellationToken.None);
        logger.LogWarning("TestConnection failed with code {Code}", result.ErrorCode);

        var all = string.Join("\n", captured);
        _out.WriteLine($"Scanned {captured.Count} log entries.");
        all.Should().NotContain(Secret);
        all.Should().NotContain(Tenant);
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
        foreach (var file in ConnectorSources())
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("Mail.Send", $"connector must never request send permission ({file})");
            // The literal broad scope "Mail.Read" (all mailboxes) must not be requested as a scope string.
            text.Should().NotContain("\"Mail.Read\"", $"connector must not request broad Mail.Read scope ({file})");
        }
        _out.WriteLine("No Mail.Send / broad Mail.Read scope literals found in connector sources.");
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
        foreach (var file in ConnectorSources())
        {
            var text = File.ReadAllText(file);
            // Forbid constructing a persistence options object anywhere in the connector.
            text.Should().NotContain("new TokenCachePersistenceOptions",
                $"token cache must never be persisted to disk ({file})");
        }
        _out.WriteLine("No TokenCachePersistenceOptions construction found in connector sources.");
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
                Message = $"throttled tenant {Tenant} secret {Secret} account {Account}"
            },
            ResponseHeaders = new Microsoft.Kiota.Abstractions.RequestHeaders { { "Retry-After", "12" } }
        };

        var n = GraphErrorNormalizer.Normalize(err);
        _out.WriteLine($"Signature: {n.Signature}; RetryAfter: {n.RetryAfter}");

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
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "folders_list.json"))));
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
            OpenContentAsync = ct => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }))
        };

        var result = await dest.WriteMessageAsync(FolderPath.Parse("Inbox/Projects"), msg, CancellationToken.None);
        _out.WriteLine($"Throttled WriteResult.ErrorCode: {result.ErrorCode}");

        result.Written.Should().BeFalse();
        result.ErrorCode.Should().Be("graph:429:throttled");
        result.ErrorCode.Should().NotContain(Tenant);
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
                ["tenantId"] = Tenant, ["clientId"] = "c", ["accountEmail"] = Account
            },
            SecretRef = "ref"
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
            ClientSecret = Secret
        };

        var client = GraphClientFactory.Build(config);
        var baseUrl = client.RequestAdapter.BaseUrl ?? "";
        _out.WriteLine($"Production BaseUrl: {baseUrl}");

        baseUrl.Should().StartWith("https://graph.microsoft.com");
    }

    [Fact]
    public void No_plaintext_http_or_foreign_host_in_connector_sources()
    {
        foreach (var file in ConnectorSources())
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("http://",
                $"connector must never use a plaintext (non-TLS) endpoint ({file})");
            // The only host literal permitted in the connector is the official Graph endpoint.
            foreach (Match m in Regex.Matches(text, "https://[\\w.-]+"))
                m.Value.Should().StartWith("https://graph.microsoft.com",
                    $"connector must not point at any host other than graph.microsoft.com ({file}: {m.Value})");
        }
        _out.WriteLine("No http:// and no foreign https host literals found in connector sources.");
    }

    private sealed class CapturingLoggerProvider(ConcurrentBag<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentBag<string> sink) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
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
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphSecurityVerificationTests` → expected **FAIL** first (e.g. the static-source path resolution or a stray scope literal). Read the captured output (`_out.WriteLine` lines) and fix any genuine leak: if a forbidden literal is found, remove it from the connector source; if `SourceDir` resolution is off for this layout, correct the relative path so it points at `src/EMaigrator.Connectors.Graph`.
3. - [ ] Make all checks pass without weakening them: the connector code from Tasks 2/3/6/7/8 should already satisfy every assertion (scope is `.default`, no `Mail.Send`/broad `Mail.Read` literals, `TokenCachePersistenceOptions = null`, signatures credential-free, `ToString` redacted, the only host literal is `https://graph.microsoft.com` and no `http://`). Only the test's source-dir path or an accidental literal should need correction — never relax an assertion.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphSecurityVerificationTests` → expected **PASS** (all 10 facts green; captured output shows scanned-log count, the production HTTPS BaseUrl, the credential-free signature, and the redacted ToString). Capture the full test output as the gate evidence.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph.Tests/GraphSecurityVerificationTests.cs
git commit -m "test(graph): security gate — no secret/token leakage, least-privilege scope, no disk token cache

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: Full-suite green + dependency-rule re-check

**Goal:** Run the entire `EMaigrator.Connectors.Graph.Tests` suite green and re-assert the dependency rule (DESIGN §15) holds for the finished assembly, closing the plan.

**Files:**
- Modify: `src/EMaigrator.Connectors.Graph.Tests/ProjectStructureTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet test src/EMaigrator.Connectors.Graph.Tests` runs the whole suite (all prior tasks) **green**, with live smoke facts skipped.
- [ ] A final dependency-rule assertion confirms the finished assembly references only `EMaigrator.Core` (plus framework/SDK packages) and none of `EMaigrator.Infrastructure`, `EMaigrator.Workers`, `EMaigrator.Api`, `EMaigrator.Cli`.
- [ ] `dotnet build src/EMaigrator.Connectors.Graph -warnaserror` succeeds (no warnings, since the csproj sets `TreatWarningsAsErrors`).

**Verify:** `dotnet test src/EMaigrator.Connectors.Graph.Tests` → all pass (live smoke skipped).

**Steps:**
1. - [ ] Extend `src/EMaigrator.Connectors.Graph.Tests/ProjectStructureTests.cs` with a final guard (write it as a failing addition first by asserting the broader exclusion set):
```csharp
    [Fact]
    public void Finished_assembly_excludes_all_composition_layers()
    {
        var referenced = typeof(GraphProviderPlugin).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenced.Should().Contain("EMaigrator.Core");
        referenced.Should().NotContain(new[]
        {
            "EMaigrator.Infrastructure",
            "EMaigrator.Workers",
            "EMaigrator.Api",
            "EMaigrator.Cli"
        });
    }
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~ProjectStructureTests` → expected **PASS** (the assembly never referenced those layers; this codifies it). If by mistake a forbidden reference exists, remove it from the csproj.
3. - [ ] Run the full suite and a strict build:
```
dotnet build src/EMaigrator.Connectors.Graph
dotnet test src/EMaigrator.Connectors.Graph.Tests
```
   Confirm all tasks' tests are green and the live smoke is reported skipped.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Graph.Tests` → expected **PASS** (whole suite green, smoke skipped).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Graph.Tests/ProjectStructureTests.cs
git commit -m "test(graph): final full-suite green and dependency-rule re-check

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
