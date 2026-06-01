# EMaigrator.Connectors.Imap Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Implement the IMAP connector plugin (`ImapProviderPlugin` + `ImapSourceProvider` + `ImapDestinationProvider`) on MailKit, covering WorkMail region presets and custom IMAP servers, `ImapBasic` and `ImapOAuthXoauth2` auth, read/list/append/exists operations mapped to the Core canonical model, and stable error-signature normalization — proven by unit tests plus Testcontainers contract/roundtrip tests against a real GreenMail IMAP server.

**Architecture:** This is a DI-discovered plugin assembly (`EMaigrator.Connectors.Imap`) that references **only** `EMaigrator.Core` abstractions (DESIGN.md §15 dependency rule) and the MailKit NuGet package. `ImapProviderPlugin` implements `IProviderPlugin` and factories the source/destination providers from a `ConnectionDescriptor` + decrypted `SecretBundle`; each provider opens one authenticated, TLS-enforced `ImapClient`, maps the IMAP folder hierarchy to `FolderPath`, streams raw RFC822 bytes via `CanonicalMessage.OpenContentAsync`, and normalizes every MailKit/protocol failure to a stable `errorSignature` string for the Core error catalog. Connection targets are validated against a provider preset/custom allowlist before any socket is opened (anti-SSRF), and credentials are never written to logs or exception messages.

**Tech Stack:** C#/.NET 10 (LTS), C# 13, nullable enabled; MailKit 4.x (`ImapClient`, `SaslMechanismOAuth2`); xUnit + FluentAssertions + NSubstitute (unit); Testcontainers + GreenMail (`greenmail/standalone`) for the IMAP contract/roundtrip tests; `Microsoft.Extensions.Logging.Abstractions` for the injected logger.

---

### Task 1: ImapConnectionSettings + WorkMail region presets and custom-server resolution

**Goal:** Provide a pure, testable resolver that turns a `ConnectionDescriptor`'s non-secret `Settings` into a validated, TLS-enforcing `ImapConnectionSettings` (host/port/SSL/auth), supporting the three WorkMail region presets via the `imap.mail.{region}.awsapps.com` host template and a custom-server path.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap/ImapConnectionSettings.cs`
- Create: `src/EMaigrator.Connectors.Imap/ImapPresets.cs`
- Create: `src/EMaigrator.Connectors.Imap/EMaigrator.Connectors.Imap.csproj` (if not already created by Plan 01 — add MailKit reference)
- Test: `src/EMaigrator.Connectors.Imap.Tests/ImapPresetsTests.cs`
- Test: `src/EMaigrator.Connectors.Imap.Tests/EMaigrator.Connectors.Imap.Tests.csproj` (if not already created by Plan 01)

**Acceptance Criteria:**
- [ ] WorkMail preset `us-east-1`, `us-west-2`, `eu-west-1` each resolve host `imap.mail.{region}.awsapps.com`, port `993`, `UseSsl = true`.
- [ ] An unknown WorkMail region throws `ImapConfigurationException` with a message that names the unknown region and does NOT contain any secret.
- [ ] Custom-server path (`preset=custom`) honors explicit `host`/`port`; defaults port to `993` and `useSsl=true` when omitted.
- [ ] Resolving custom-server with `useSsl=false` AND `allowPlaintext` unset/false throws `ImapConfigurationException` (TLS enforced by default).
- [ ] Resolving custom-server with `useSsl=false` AND `allowPlaintext=true` succeeds (explicit opt-in only).
- [ ] `ImapConnectionSettings` exposes resolved `Host`, `Port`, `UseSsl`, `AccountEmail`.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapPresets` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.Tests/ImapPresetsTests.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapPresetsTests
{
    private static ConnectionDescriptor Descriptor(IReadOnlyDictionary<string, string> settings)
        => new()
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = settings,
            SecretRef = "secret/abc",
        };

    [Theory]
    [InlineData("us-east-1", "imap.mail.us-east-1.awsapps.com")]
    [InlineData("us-west-2", "imap.mail.us-west-2.awsapps.com")]
    [InlineData("eu-west-1", "imap.mail.eu-west-1.awsapps.com")]
    public void Resolves_workmail_region_preset_to_host_993_ssl(string region, string expectedHost)
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "workmail",
            ["region"] = region,
            ["accountEmail"] = "user@corp.example",
        });

        var settings = ImapPresets.Resolve(d);

        settings.Host.Should().Be(expectedHost);
        settings.Port.Should().Be(993);
        settings.UseSsl.Should().BeTrue();
        settings.AccountEmail.Should().Be("user@corp.example");
    }

    [Fact]
    public void Unknown_workmail_region_throws_naming_region_without_secret()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "workmail",
            ["region"] = "mars-east-9",
            ["accountEmail"] = "user@corp.example",
        });

        var act = () => ImapPresets.Resolve(d);

        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain("mars-east-9").And.NotContain("secret/abc");
    }

    [Fact]
    public void Custom_server_defaults_to_993_ssl()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "imap.zoho.com",
            ["accountEmail"] = "user@zoho.example",
        });

        var settings = ImapPresets.Resolve(d);

        settings.Host.Should().Be("imap.zoho.com");
        settings.Port.Should().Be(993);
        settings.UseSsl.Should().BeTrue();
    }

    [Fact]
    public void Custom_server_honors_explicit_host_and_port()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "mail.example.org",
            ["port"] = "1993",
            ["accountEmail"] = "u@example.org",
        });

        var settings = ImapPresets.Resolve(d);

        settings.Host.Should().Be("mail.example.org");
        settings.Port.Should().Be(1993);
        settings.UseSsl.Should().BeTrue();
    }

    [Fact]
    public void Plaintext_without_explicit_optin_is_rejected()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "mail.example.org",
            ["port"] = "143",
            ["useSsl"] = "false",
            ["accountEmail"] = "u@example.org",
        });

        var act = () => ImapPresets.Resolve(d);

        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain("TLS");
    }

    [Fact]
    public void Plaintext_with_explicit_optin_is_allowed()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "mail.example.org",
            ["port"] = "143",
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = "u@example.org",
        });

        var settings = ImapPresets.Resolve(d);

        settings.UseSsl.Should().BeFalse();
        settings.Port.Should().Be(143);
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapPresets`. Expected FAIL: `ImapPresets`, `ImapConnectionSettings`, and `ImapConfigurationException` do not exist (CS0103/CS0246 compile errors).
3. - [ ] Create `src/EMaigrator.Connectors.Imap/ImapConnectionSettings.cs`:
```csharp
namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Resolved, validated IMAP endpoint settings. Contains no secret material —
/// the password / OAuth token lives only in the transient <c>SecretBundle</c>.
/// </summary>
public sealed record ImapConnectionSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool UseSsl { get; init; }
    public required string AccountEmail { get; init; }
}
```
   Create `src/EMaigrator.Connectors.Imap/ImapConfigurationException.cs`:
```csharp
namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Thrown when a <c>ConnectionDescriptor</c> cannot be turned into valid IMAP
/// settings (unknown region, disallowed host, plaintext without opt-in).
/// Messages MUST NOT contain credentials or secret refs beyond the offending
/// non-secret value.
/// </summary>
public sealed class ImapConfigurationException : Exception
{
    public ImapConfigurationException(string message) : base(message) { }
}
```
   Create `src/EMaigrator.Connectors.Imap/ImapPresets.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Turns a <see cref="ConnectionDescriptor"/>'s non-secret settings into
/// validated <see cref="ImapConnectionSettings"/>. Enforces TLS by default and
/// supplies the WorkMail region host template (anti-SSRF allowlisting lives in
/// <see cref="ImapHostValidator"/>, applied at connect time).
/// </summary>
public static class ImapPresets
{
    // The only AWS regions where WorkMail offers IMAP today.
    public static readonly IReadOnlyDictionary<string, string> WorkMailRegions =
        new Dictionary<string, string>
        {
            ["us-east-1"] = "imap.mail.us-east-1.awsapps.com",
            ["us-west-2"] = "imap.mail.us-west-2.awsapps.com",
            ["eu-west-1"] = "imap.mail.eu-west-1.awsapps.com",
        };

    public static ImapConnectionSettings Resolve(ConnectionDescriptor descriptor)
    {
        var s = descriptor.Settings;
        var accountEmail = Get(s, "accountEmail")
            ?? throw new ImapConfigurationException("Missing required setting 'accountEmail'.");
        var preset = Get(s, "preset")?.ToLowerInvariant() ?? "custom";

        if (preset == "workmail")
        {
            var region = Get(s, "region")
                ?? throw new ImapConfigurationException("WorkMail preset requires a 'region' setting.");
            if (!WorkMailRegions.TryGetValue(region, out var host))
            {
                throw new ImapConfigurationException(
                    $"Unknown WorkMail region '{region}'. Supported regions: " +
                    string.Join(", ", WorkMailRegions.Keys) + ".");
            }
            return new ImapConnectionSettings
            {
                Host = host,
                Port = 993,
                UseSsl = true,
                AccountEmail = accountEmail,
            };
        }

        // custom server
        var customHost = Get(s, "host")
            ?? throw new ImapConfigurationException("Custom IMAP server requires a 'host' setting.");
        var useSsl = ParseBool(Get(s, "useSsl"), defaultValue: true);
        var allowPlaintext = ParseBool(Get(s, "allowPlaintext"), defaultValue: false);
        var port = ParseInt(Get(s, "port"), defaultValue: useSsl ? 993 : 143);

        if (!useSsl && !allowPlaintext)
        {
            throw new ImapConfigurationException(
                "Refusing to connect without TLS. Set 'allowPlaintext=true' to explicitly opt in to an insecure connection.");
        }

        return new ImapConnectionSettings
        {
            Host = customHost,
            Port = port,
            UseSsl = useSsl,
            AccountEmail = accountEmail,
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string> s, string key)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static bool ParseBool(string? value, bool defaultValue)
        => value is null ? defaultValue : bool.Parse(value);

    private static int ParseInt(string? value, int defaultValue)
        => value is null ? defaultValue : int.Parse(value);
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapPresets`. Expected PASS: all 8 cases green.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap/ImapConnectionSettings.cs src/EMaigrator.Connectors.Imap/ImapConfigurationException.cs src/EMaigrator.Connectors.Imap/ImapPresets.cs src/EMaigrator.Connectors.Imap.Tests/ImapPresetsTests.cs
git commit -m "feat(imap): resolve WorkMail region presets and custom-server settings with TLS enforcement

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: ImapHostValidator — anti-SSRF allowlist for connection targets

**Goal:** Validate that a resolved target host is allowed for the descriptor (WorkMail preset host template OR an operator-supplied custom-host allowlist) before any socket is opened, so a test-connection / migration cannot be coerced into connecting to an arbitrary internal host.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap/ImapHostValidator.cs`
- Test: `src/EMaigrator.Connectors.Imap.Tests/ImapHostValidatorTests.cs`

**Acceptance Criteria:**
- [ ] For `preset=workmail`, only the exact `imap.mail.{region}.awsapps.com` host for the resolved region passes; any other host throws `ImapConfigurationException`.
- [ ] For `preset=custom`, the resolved host must equal the descriptor's `host` setting (no rewrite) and must not be a loopback/link-local/metadata address literal (`127.0.0.1`, `::1`, `169.254.169.254`) unless `allowPlaintext=true` is explicitly set (self-host/test escape hatch).
- [ ] Validation rejects hosts containing scheme/path/credential characters (`/`, `@`, `:` beyond port, whitespace).
- [ ] Exception message names the rejected host and contains no secret.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapHostValidator` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.Tests/ImapHostValidatorTests.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapHostValidatorTests
{
    private static ConnectionDescriptor Workmail(string region) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "workmail",
            ["region"] = region,
            ["accountEmail"] = "u@corp.example",
        },
    };

    private static ConnectionDescriptor Custom(string host, bool allowPlaintext = false) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = host,
            ["accountEmail"] = "u@example.org",
            ["allowPlaintext"] = allowPlaintext ? "true" : "false",
            ["useSsl"] = allowPlaintext ? "false" : "true",
        },
    };

    [Fact]
    public void Workmail_allows_only_the_canonical_region_host()
    {
        var d = Workmail("eu-west-1");
        var act = () => ImapHostValidator.Validate(d, "imap.mail.eu-west-1.awsapps.com");
        act.Should().NotThrow();
    }

    [Fact]
    public void Workmail_rejects_host_that_is_not_the_region_template()
    {
        var d = Workmail("eu-west-1");
        var act = () => ImapHostValidator.Validate(d, "evil.internal.corp");
        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain("evil.internal.corp");
    }

    [Fact]
    public void Custom_allows_declared_host()
    {
        var d = Custom("imap.zoho.com");
        var act = () => ImapHostValidator.Validate(d, "imap.zoho.com");
        act.Should().NotThrow();
    }

    [Fact]
    public void Custom_rejects_host_other_than_declared()
    {
        var d = Custom("imap.zoho.com");
        var act = () => ImapHostValidator.Validate(d, "169.254.169.254");
        act.Should().Throw<ImapConfigurationException>();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("169.254.169.254")]
    public void Custom_rejects_metadata_and_loopback_without_optin(string host)
    {
        var d = Custom(host);
        var act = () => ImapHostValidator.Validate(d, host);
        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain(host);
    }

    [Fact]
    public void Custom_allows_loopback_with_explicit_plaintext_optin()
    {
        var d = Custom("127.0.0.1", allowPlaintext: true);
        var act = () => ImapHostValidator.Validate(d, "127.0.0.1");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("imap.zoho.com/evil")]
    [InlineData("user@imap.zoho.com")]
    [InlineData("imap.zoho.com ")]
    public void Rejects_hosts_with_scheme_path_or_credential_characters(string host)
    {
        var d = Custom(host);
        var act = () => ImapHostValidator.Validate(d, host);
        act.Should().Throw<ImapConfigurationException>();
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapHostValidator`. Expected FAIL: `ImapHostValidator` does not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Imap/ImapHostValidator.cs`:
```csharp
using System;
using System.Linq;
using System.Net;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Anti-SSRF guard. A test-connection or migration may only connect to the host
/// that the provider preset prescribes (WorkMail) or that the operator explicitly
/// declared (custom). Blocks loopback/link-local/cloud-metadata literals unless an
/// explicit plaintext opt-in is present (self-host / test escape hatch).
/// </summary>
public static class ImapHostValidator
{
    private static readonly char[] ForbiddenHostChars = { '/', '@', '\\', ' ', '\t', '\r', '\n', '?', '#' };

    public static void Validate(ConnectionDescriptor descriptor, string resolvedHost)
    {
        if (string.IsNullOrWhiteSpace(resolvedHost))
            throw new ImapConfigurationException("Resolved IMAP host is empty.");

        if (resolvedHost.IndexOfAny(ForbiddenHostChars) >= 0 || resolvedHost.Contains("//"))
            throw new ImapConfigurationException(
                $"Refusing to connect to malformed host '{resolvedHost}'.");

        var s = descriptor.Settings;
        var preset = (s.TryGetValue("preset", out var p) ? p : "custom").ToLowerInvariant();

        if (preset == "workmail")
        {
            var region = s.TryGetValue("region", out var r) ? r : null;
            if (region is null || !ImapPresets.WorkMailRegions.TryGetValue(region, out var expected))
                throw new ImapConfigurationException(
                    $"WorkMail region '{region}' is not a known IMAP region.");
            if (!string.Equals(resolvedHost, expected, StringComparison.OrdinalIgnoreCase))
                throw new ImapConfigurationException(
                    $"Refusing to connect to '{resolvedHost}': WorkMail preset only permits '{expected}'.");
            return;
        }

        // custom: host must match what the operator declared (no silent rewrite)
        var declared = s.TryGetValue("host", out var h) ? h : null;
        if (declared is null || !string.Equals(resolvedHost, declared, StringComparison.OrdinalIgnoreCase))
            throw new ImapConfigurationException(
                $"Refusing to connect to '{resolvedHost}': it does not match the declared host '{declared}'.");

        var allowPlaintext = s.TryGetValue("allowPlaintext", out var ap) && bool.TryParse(ap, out var b) && b;
        if (!allowPlaintext && IsBlockedLiteral(resolvedHost))
            throw new ImapConfigurationException(
                $"Refusing to connect to internal/metadata address '{resolvedHost}'.");
    }

    private static bool IsBlockedLiteral(string host)
    {
        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            return false; // a DNS name; not a literal we block here
        if (IPAddress.IsLoopback(ip)) return true;
        var bytes = ip.GetAddressBytes();
        // IPv4 link-local 169.254.0.0/16 (includes 169.254.169.254 metadata)
        if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) return true;
        // IPv6 link-local fe80::/10
        if (bytes.Length == 16 && bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
        return false;
    }
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapHostValidator`. Expected PASS: all cases green.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap/ImapHostValidator.cs src/EMaigrator.Connectors.Imap.Tests/ImapHostValidatorTests.cs
git commit -m "feat(imap): validate connection target host against preset/custom allowlist (anti-SSRF)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: ImapErrorNormalizer — stable errorSignature mapping

**Goal:** Normalize MailKit/protocol/socket exceptions into a stable `errorSignature` string (per CONTRACTS §8: "provider code + condition") for the Core error catalog, guaranteeing the signature never contains credentials.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap/ImapErrorNormalizer.cs`
- Test: `src/EMaigrator.Connectors.Imap.Tests/ImapErrorNormalizerTests.cs`

**Acceptance Criteria:**
- [ ] An `MailKit.Security.AuthenticationException` normalizes to `imap:auth-failed`.
- [ ] An `MailKit.Security.SslHandshakeException` normalizes to `imap:tls-handshake-failed`.
- [ ] A `System.Net.Sockets.SocketException` normalizes to `imap:connect-failed`.
- [ ] A `MailKit.Net.Imap.ImapCommandException` with response `NO`/`BAD` normalizes to `imap:command-<response>` (e.g. `imap:command-no`).
- [ ] An `OperationCanceledException` rethrows unchanged (cancellation is not an error signature).
- [ ] The normalizer redacts: given an exception whose message contains the account password, the produced signature does NOT contain the password substring.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapErrorNormalizer` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.Tests/ImapErrorNormalizerTests.cs`:
```csharp
using System;
using System.Net.Sockets;
using EMaigrator.Connectors.Imap;
using FluentAssertions;
using MailKit.Net.Imap;
using MailKit.Security;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapErrorNormalizerTests
{
    [Fact]
    public void Auth_exception_maps_to_auth_failed()
    {
        var sig = ImapErrorNormalizer.Normalize(new AuthenticationException("bad login"));
        sig.Should().Be("imap:auth-failed");
    }

    [Fact]
    public void Ssl_handshake_maps_to_tls_handshake_failed()
    {
        var sig = ImapErrorNormalizer.Normalize(new SslHandshakeException("tls"));
        sig.Should().Be("imap:tls-handshake-failed");
    }

    [Fact]
    public void Socket_exception_maps_to_connect_failed()
    {
        var sig = ImapErrorNormalizer.Normalize(new SocketException(10061));
        sig.Should().Be("imap:connect-failed");
    }

    [Fact]
    public void Command_exception_maps_to_command_response()
    {
        var ex = new ImapCommandException(ImapCommandResponse.No, "SELECT", "mailbox does not exist");
        var sig = ImapErrorNormalizer.Normalize(ex);
        sig.Should().Be("imap:command-no");
    }

    [Fact]
    public void Cancellation_is_rethrown_not_normalized()
    {
        var act = () => ImapErrorNormalizer.Normalize(new OperationCanceledException());
        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Signature_never_contains_credential_substring()
    {
        const string password = "Sup3rSecretAppPassw0rd!";
        var ex = new AuthenticationException($"LOGIN failed for password {password}");
        var sig = ImapErrorNormalizer.Normalize(ex);
        sig.Should().NotContain(password);
    }

    [Fact]
    public void Unknown_exception_maps_to_unknown()
    {
        var sig = ImapErrorNormalizer.Normalize(new InvalidOperationException("weird"));
        sig.Should().Be("imap:unknown");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapErrorNormalizer`. Expected FAIL: `ImapErrorNormalizer` does not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Imap/ImapErrorNormalizer.cs`:
```csharp
using System;
using System.Net.Sockets;
using MailKit.Net.Imap;
using MailKit.Security;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Maps transport-layer exceptions to a stable, credential-free
/// <c>errorSignature</c> string ("imap:&lt;condition&gt;") for the Core error
/// catalog (CONTRACTS §8). The signature is derived only from exception TYPE and
/// well-known protocol response codes — never from free-form message text — so a
/// credential embedded in a message can never leak into the signature.
/// </summary>
public static class ImapErrorNormalizer
{
    public static string Normalize(Exception ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                throw ex; // cancellation is control flow, not an error signature
            case AuthenticationException:
                return "imap:auth-failed";
            case SslHandshakeException:
                return "imap:tls-handshake-failed";
            case ImapCommandException cmd:
                return $"imap:command-{cmd.Response.ToString().ToLowerInvariant()}";
            case ImapProtocolException:
                return "imap:protocol-error";
            case SocketException:
                return "imap:connect-failed";
            case ServiceNotConnectedException:
                return "imap:not-connected";
            case ServiceNotAuthenticatedException:
                return "imap:not-authenticated";
            case TimeoutException:
                return "imap:timeout";
            default:
                return "imap:unknown";
        }
    }
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapErrorNormalizer`. Expected PASS: all cases green.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap/ImapErrorNormalizer.cs src/EMaigrator.Connectors.Imap.Tests/ImapErrorNormalizerTests.cs
git commit -m "feat(imap): normalize protocol exceptions to stable credential-free error signatures

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: ImapConstraints + ImapClientFactory (auth: ImapBasic & ImapOAuthXoauth2)

**Goal:** Provide the IMAP `ProviderConstraints` (used by pre-flight against the destination) and a single `ImapClientFactory` that opens one TLS-validated, authenticated `ImapClient` for either `ImapBasic` (LOGIN) or `ImapOAuthXoauth2` (`SaslMechanismOAuth2`) — never logging the secret.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap/ImapConstraints.cs`
- Create: `src/EMaigrator.Connectors.Imap/ImapClientFactory.cs`
- Test: `src/EMaigrator.Connectors.Imap.Tests/ImapConstraintsTests.cs`

**Acceptance Criteria:**
- [ ] `ImapConstraints.Default` returns a `ProviderConstraints` with `FolderSeparator` configurable (default `'/'`), `MaxFolderDepth = int.MaxValue` (IMAP imposes no hard depth), and an `IllegalNameChars` set that at minimum excludes the configured separator and control chars.
- [ ] `ImapClientFactory.BuildOAuth2Mechanism(accountEmail, token)` returns a `SaslMechanismOAuth2` whose name is `XOAUTH2`.
- [ ] `ImapClientFactory.RequireSecret(bundle, key)` returns the value when present and throws `ImapConfigurationException` (message does NOT contain the value of any other key) when absent.
- [ ] No factory method writes the secret to the injected logger (asserted via a fake logger capturing zero entries containing the secret in Task 8's security test; here we assert `RequireSecret` does not log at all by construction — pure function).

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapConstraints` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.Tests/ImapConstraintsTests.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using FluentAssertions;
using MailKit.Security;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapConstraintsTests
{
    [Fact]
    public void Default_constraints_have_no_hard_depth_and_known_separator()
    {
        var c = ImapConstraints.Default('/');
        c.FolderSeparator.Should().Be('/');
        c.MaxFolderDepth.Should().Be(int.MaxValue);
        c.IllegalNameChars.Should().Contain('/');
    }

    [Fact]
    public void Build_oauth2_mechanism_uses_xoauth2()
    {
        var mech = ImapClientFactory.BuildOAuth2Mechanism("u@corp.example", "ya29.token");
        mech.Should().BeOfType<SaslMechanismOAuth2>();
        mech.MechanismName.Should().Be("XOAUTH2");
    }

    [Fact]
    public void Require_secret_returns_present_value()
    {
        var values = new Dictionary<string, string> { ["password"] = "p@ss" };
        ImapClientFactory.RequireSecret(values, "password").Should().Be("p@ss");
    }

    [Fact]
    public void Require_secret_missing_throws_without_leaking_other_values()
    {
        var values = new Dictionary<string, string> { ["password"] = "p@ss" };
        var act = () => ImapClientFactory.RequireSecret(values, "accessToken");
        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().NotContain("p@ss");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapConstraints`. Expected FAIL: `ImapConstraints` and `ImapClientFactory` do not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Imap/ImapConstraints.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Constraints the IMAP transport imposes (DESIGN.md §7 — used by pre-flight when
/// IMAP is the destination). IMAP itself imposes no hard depth/size limit; the
/// real ceilings belong to the concrete server, so defaults are permissive.
/// </summary>
public static class ImapConstraints
{
    public static ProviderConstraints Default(char separator = '/') => new()
    {
        MaxFolderDepth = int.MaxValue,
        MaxPathLengthChars = int.MaxValue,
        IllegalNameChars = BuildIllegalChars(separator),
        MaxMessageBytes = long.MaxValue,
        MaxAttachmentBytes = long.MaxValue,
        FolderSeparator = separator,
        ReservedFolderNames = new[] { "INBOX" },
    };

    private static IReadOnlyCollection<char> BuildIllegalChars(char separator)
    {
        var set = new HashSet<char> { separator, '\0', '\r', '\n', '\t' };
        return set;
    }
}
```
   Create `src/EMaigrator.Connectors.Imap/ImapClientFactory.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Opens one authenticated <see cref="ImapClient"/> for the resolved settings.
/// Validates the target host (anti-SSRF), enforces TLS, and authenticates with
/// either LOGIN (basic / app-password) or XOAUTH2. Secrets are pulled from the
/// transient <see cref="SecretBundle"/> and never logged.
/// </summary>
public static class ImapClientFactory
{
    public static SaslMechanismOAuth2 BuildOAuth2Mechanism(string accountEmail, string accessToken)
        => new(accountEmail, accessToken);

    public static string RequireSecret(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            return v;
        throw new ImapConfigurationException($"Required secret '{key}' was not present in the secret bundle.");
    }

    public static async Task<ImapClient> ConnectAndAuthenticateAsync(
        ConnectionDescriptor descriptor,
        ImapConnectionSettings settings,
        SecretBundle secrets,
        ILogger logger,
        CancellationToken ct)
    {
        // Anti-SSRF: the only host we may dial is the one the preset/allowlist permits.
        ImapHostValidator.Validate(descriptor, settings.Host);

        var client = new ImapClient();
        try
        {
            var secureOption = settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
            logger.LogInformation(
                "Connecting to IMAP host {Host}:{Port} (ssl={UseSsl}) for {Account}",
                settings.Host, settings.Port, settings.UseSsl, settings.AccountEmail);
            await client.ConnectAsync(settings.Host, settings.Port, secureOption, ct).ConfigureAwait(false);

            if (descriptor.Auth == AuthMethod.ImapOAuthXoauth2)
            {
                var token = RequireSecret(secrets.Values, "accessToken");
                await client.AuthenticateAsync(BuildOAuth2Mechanism(settings.AccountEmail, token), ct)
                    .ConfigureAwait(false);
            }
            else // ImapBasic
            {
                var password = RequireSecret(secrets.Values, "password");
                await client.AuthenticateAsync(settings.AccountEmail, password, ct).ConfigureAwait(false);
            }

            return client;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            // Re-throw a sanitized exception: signature only, original as inner is unsafe to log,
            // so we wrap without the credential-bearing original message.
            throw new ImapTransportException(ImapErrorNormalizer.Normalize(ex));
        }
    }
}
```
   Create `src/EMaigrator.Connectors.Imap/ImapTransportException.cs`:
```csharp
namespace EMaigrator.Connectors.Imap;

/// <summary>
/// A transport failure already normalized to a credential-free
/// <c>errorSignature</c>. The <see cref="Signature"/> is safe to log and to feed
/// the Core error catalog.
/// </summary>
public sealed class ImapTransportException : Exception
{
    public string Signature { get; }
    public ImapTransportException(string signature) : base(signature) => Signature = signature;
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapConstraints`. Expected PASS: all cases green.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap/ImapConstraints.cs src/EMaigrator.Connectors.Imap/ImapClientFactory.cs src/EMaigrator.Connectors.Imap/ImapTransportException.cs src/EMaigrator.Connectors.Imap.Tests/ImapConstraintsTests.cs
git commit -m "feat(imap): IMAP constraints + TLS-validated client factory for basic and XOAUTH2 auth

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: ImapFolderMapper — IMAP hierarchy ↔ FolderPath

**Goal:** Map between an IMAP folder's server full-name (using the server's hierarchy delimiter) and the canonical `FolderPath` ('/'-joined), and back, so list/read/ensure operations agree on folder identity.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap/ImapFolderMapper.cs`
- Test: `src/EMaigrator.Connectors.Imap.Tests/ImapFolderMapperTests.cs`

**Acceptance Criteria:**
- [ ] `ToFolderPath("INBOX/Projects/2026", '/')` → `FolderPath` with segments `["INBOX","Projects","2026"]`.
- [ ] `ToFolderPath("INBOX.Projects.2026", '.')` (dot-delimited server) → segments `["INBOX","Projects","2026"]`.
- [ ] `ToServerName(FolderPath["INBOX","Projects"], '.')` → `"INBOX.Projects"`.
- [ ] Root `FolderPath` (empty segments) maps to `""` server name.
- [ ] A segment containing the delimiter is preserved as a single segment (no accidental split) by relying on MailKit's per-folder names, not raw string splitting — verified by mapping a `["A/B"]`-style segment through `ToServerName` with `'.'` delimiter yielding `"A/B"` unchanged.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapFolderMapper` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.Tests/ImapFolderMapperTests.cs`:
```csharp
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapFolderMapperTests
{
    [Fact]
    public void Slash_delimited_server_name_maps_to_segments()
    {
        var fp = ImapFolderMapper.ToFolderPath("INBOX/Projects/2026", '/');
        fp.Segments.Should().Equal("INBOX", "Projects", "2026");
    }

    [Fact]
    public void Dot_delimited_server_name_maps_to_segments()
    {
        var fp = ImapFolderMapper.ToFolderPath("INBOX.Projects.2026", '.');
        fp.Segments.Should().Equal("INBOX", "Projects", "2026");
    }

    [Fact]
    public void Folder_path_maps_back_to_dot_server_name()
    {
        var fp = new FolderPath(new[] { "INBOX", "Projects" });
        ImapFolderMapper.ToServerName(fp, '.').Should().Be("INBOX.Projects");
    }

    [Fact]
    public void Root_maps_to_empty_server_name()
    {
        var fp = new FolderPath(System.Array.Empty<string>());
        ImapFolderMapper.ToServerName(fp, '.').Should().Be("");
    }

    [Fact]
    public void Segment_containing_other_delimiter_is_preserved()
    {
        var fp = new FolderPath(new[] { "A/B" });
        ImapFolderMapper.ToServerName(fp, '.').Should().Be("A/B");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapFolderMapper`. Expected FAIL: `ImapFolderMapper` does not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Imap/ImapFolderMapper.cs`:
```csharp
using System;
using System.Collections.Generic;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Translates between an IMAP server's hierarchical full-name (which uses the
/// server-reported delimiter) and the canonical '/'-joined <see cref="FolderPath"/>.
/// </summary>
public static class ImapFolderMapper
{
    public static FolderPath ToFolderPath(string serverFullName, char delimiter)
    {
        if (string.IsNullOrEmpty(serverFullName))
            return new FolderPath(Array.Empty<string>());

        var segments = serverFullName.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        return new FolderPath(segments);
    }

    public static string ToServerName(FolderPath path, char delimiter)
    {
        if (path.IsRoot)
            return string.Empty;
        return string.Join(delimiter, (IEnumerable<string>)path.Segments);
    }
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapFolderMapper`. Expected PASS: all cases green.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap/ImapFolderMapper.cs src/EMaigrator.Connectors.Imap.Tests/ImapFolderMapperTests.cs
git commit -m "feat(imap): map IMAP folder hierarchy to canonical FolderPath and back

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: ImapMessageMapper — IMAP flags ↔ MessageFlags and identity-key construction

**Goal:** Map MailKit `MessageFlags` to/from the Core `MessageFlags` enum and build a `MessageIdentityInput` from a fetched message's envelope (Message-ID, From, To, Subject, Date) so `ImapSourceProvider` sets `CanonicalMessage.IdentityKey` via `IdentityKey.Compute`.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap/ImapMessageMapper.cs`
- Test: `src/EMaigrator.Connectors.Imap.Tests/ImapMessageMapperTests.cs`

**Acceptance Criteria:**
- [ ] MailKit `MessageFlags.Seen | Answered | Flagged | Draft | Deleted` maps to Core `MessageFlags.Seen | Answered | Flagged | Draft | Deleted` (bit-for-bit semantic mapping; MailKit `Recent` is ignored).
- [ ] Core `MessageFlags` maps back to MailKit `MessageFlags` for APPEND, preserving Seen/Answered/Flagged/Draft/Deleted.
- [ ] `BuildIdentityInput` returns a `MessageIdentityInput` whose `MessageId` is the envelope Message-ID when present and `DecodedBodySha256Hex` is the supplied hex (caller computes over decoded body), and whose `From`/`To`/`Subject`/`Date` are populated from the envelope.
- [ ] When the envelope Message-ID is null/empty, `BuildIdentityInput.MessageId` is null (so `IdentityKey.Compute` falls back to the composite hash).

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapMessageMapper` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.Tests/ImapMessageMapperTests.cs`:
```csharp
using System;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Idempotency;
using FluentAssertions;
using Xunit;
using CoreFlags = EMaigrator.Core.Model.MessageFlags;
using Mk = MailKit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapMessageMapperTests
{
    [Fact]
    public void MailKit_flags_map_to_core_flags()
    {
        var mk = Mk.MessageFlags.Seen | Mk.MessageFlags.Answered | Mk.MessageFlags.Flagged
                 | Mk.MessageFlags.Draft | Mk.MessageFlags.Deleted | Mk.MessageFlags.Recent;
        var core = ImapMessageMapper.ToCoreFlags(mk);
        core.Should().Be(CoreFlags.Seen | CoreFlags.Answered | CoreFlags.Flagged | CoreFlags.Draft | CoreFlags.Deleted);
    }

    [Fact]
    public void Core_flags_map_back_to_mailkit_flags()
    {
        var core = CoreFlags.Seen | CoreFlags.Flagged;
        var mk = ImapMessageMapper.ToMailKitFlags(core);
        mk.HasFlag(Mk.MessageFlags.Seen).Should().BeTrue();
        mk.HasFlag(Mk.MessageFlags.Flagged).Should().BeTrue();
        mk.HasFlag(Mk.MessageFlags.Answered).Should().BeFalse();
    }

    [Fact]
    public void Build_identity_input_uses_message_id_and_body_hash()
    {
        var input = ImapMessageMapper.BuildIdentityInput(
            messageId: "<abc@corp.example>",
            from: "a@corp.example",
            to: "b@corp.example",
            subject: "Hello",
            date: DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            decodedBodySha256Hex: "deadbeef");

        input.MessageId.Should().Be("<abc@corp.example>");
        input.DecodedBodySha256Hex.Should().Be("deadbeef");
        input.From.Should().Be("a@corp.example");
        input.Subject.Should().Be("Hello");

        // and IdentityKey.Compute (Core) prefers the Message-ID
        IdentityKey.Compute(input).Should().StartWith("mid:");
    }

    [Fact]
    public void Build_identity_input_null_message_id_falls_back_to_hash()
    {
        var input = ImapMessageMapper.BuildIdentityInput(
            messageId: null,
            from: "a@corp.example",
            to: "b@corp.example",
            subject: "Hello",
            date: DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            decodedBodySha256Hex: "deadbeef");

        input.MessageId.Should().BeNull();
        IdentityKey.Compute(input).Should().StartWith("h:");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapMessageMapper`. Expected FAIL: `ImapMessageMapper` does not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Imap/ImapMessageMapper.cs`:
```csharp
using System;
using EMaigrator.Core.Idempotency;
using CoreFlags = EMaigrator.Core.Model.MessageFlags;
using MkFlags = MailKit.MessageFlags;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Maps IMAP message metadata to the canonical model: flag translation and
/// idempotency-input construction. Body bytes are never read here — the body
/// hash is supplied by the caller (streaming pass-through; DESIGN.md §6/§10).
/// </summary>
public static class ImapMessageMapper
{
    public static CoreFlags ToCoreFlags(MkFlags flags)
    {
        var result = CoreFlags.None;
        if (flags.HasFlag(MkFlags.Seen)) result |= CoreFlags.Seen;
        if (flags.HasFlag(MkFlags.Answered)) result |= CoreFlags.Answered;
        if (flags.HasFlag(MkFlags.Flagged)) result |= CoreFlags.Flagged;
        if (flags.HasFlag(MkFlags.Draft)) result |= CoreFlags.Draft;
        if (flags.HasFlag(MkFlags.Deleted)) result |= CoreFlags.Deleted;
        return result;
    }

    public static MkFlags ToMailKitFlags(CoreFlags flags)
    {
        var result = MkFlags.None;
        if (flags.HasFlag(CoreFlags.Seen)) result |= MkFlags.Seen;
        if (flags.HasFlag(CoreFlags.Answered)) result |= MkFlags.Answered;
        if (flags.HasFlag(CoreFlags.Flagged)) result |= MkFlags.Flagged;
        if (flags.HasFlag(CoreFlags.Draft)) result |= MkFlags.Draft;
        if (flags.HasFlag(CoreFlags.Deleted)) result |= MkFlags.Deleted;
        return result;
    }

    public static MessageIdentityInput BuildIdentityInput(
        string? messageId, string? from, string? to, string? subject,
        DateTimeOffset? date, string decodedBodySha256Hex)
        => new()
        {
            MessageId = string.IsNullOrWhiteSpace(messageId) ? null : messageId,
            From = from,
            To = to,
            Subject = subject,
            Date = date,
            DecodedBodySha256Hex = decodedBodySha256Hex,
        };
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapMessageMapper`. Expected PASS: all cases green.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap/ImapMessageMapper.cs src/EMaigrator.Connectors.Imap.Tests/ImapMessageMapperTests.cs
git commit -m "feat(imap): map IMAP flags to canonical MessageFlags and build idempotency input

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: ImapSourceProvider, ImapDestinationProvider, ImapProviderPlugin (+ DI extension)

**Goal:** Implement the three CONTRACTS §2 types end-to-end on MailKit — `ImapSourceProvider` (TestConnection/ListFolders/ReadMessages), `ImapDestinationProvider` (TestConnection/EnsureFolder/WriteMessage/ExistsByMessageId), and `ImapProviderPlugin` (factories + DI `AddImapConnector()`) — with `OpenContentAsync` streaming raw RFC822 bytes and APPEND preserving `InternalDate` + flags. These are exercised by the live GreenMail contract tests in Tasks 8–10.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap/ImapSourceProvider.cs`
- Create: `src/EMaigrator.Connectors.Imap/ImapDestinationProvider.cs`
- Create: `src/EMaigrator.Connectors.Imap/ImapProviderPlugin.cs`
- Create: `src/EMaigrator.Connectors.Imap/ServiceCollectionExtensions.cs`
- Test: `src/EMaigrator.Connectors.Imap.Tests/ImapProviderPluginTests.cs`

**Acceptance Criteria:**
- [ ] `ImapProviderPlugin.Id` is `new ProviderId("imap")`; `SupportedAuth` = `{ ImapBasic, ImapOAuthXoauth2 }`; `CanBeSource` and `CanBeDestination` both true.
- [ ] `CreateSource`/`CreateDestination` return providers whose `Id` is `imap` and whose `Constraints.FolderSeparator` reflects the server delimiter once connected (default `'/'` before connect).
- [ ] `AddImapConnector(IServiceCollection)` registers exactly one `IProviderPlugin` of type `ImapProviderPlugin`.
- [ ] Each provider implements `IAsyncDisposable` and disconnects/disposes its `ImapClient`.
- [ ] (Compile-level) `ImapSourceProvider : ISourceProvider`, `ImapDestinationProvider : IDestinationProvider` — exact CONTRACTS §2 signatures, no deviation.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapProviderPlugin` → all pass.

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.Tests/ImapProviderPluginTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapProviderPluginTests
{
    private static ConnectionDescriptor BasicDescriptor() => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "imap.example.org",
            ["accountEmail"] = "u@example.org",
        },
        SecretRef = "secret/x",
    };

    private static SecretBundle Secret() =>
        new(new Dictionary<string, string> { ["password"] = "p@ss" });

    [Fact]
    public void Plugin_advertises_imap_identity_and_capabilities()
    {
        var plugin = new ImapProviderPlugin();
        plugin.Id.Should().Be(new ProviderId("imap"));
        plugin.SupportedAuth.Should().BeEquivalentTo(new[] { AuthMethod.ImapBasic, AuthMethod.ImapOAuthXoauth2 });
        plugin.CanBeSource.Should().BeTrue();
        plugin.CanBeDestination.Should().BeTrue();
    }

    [Fact]
    public void Create_source_returns_imap_source_provider()
    {
        var plugin = new ImapProviderPlugin();
        var src = plugin.CreateSource(BasicDescriptor(), Secret());
        src.Should().BeAssignableTo<ISourceProvider>();
        src.Id.Should().Be(new ProviderId("imap"));
        src.Constraints.FolderSeparator.Should().Be('/');
    }

    [Fact]
    public void Create_destination_returns_imap_destination_provider()
    {
        var plugin = new ImapProviderPlugin();
        var dst = plugin.CreateDestination(BasicDescriptor(), Secret());
        dst.Should().BeAssignableTo<IDestinationProvider>();
        dst.Id.Should().Be(new ProviderId("imap"));
    }

    [Fact]
    public void AddImapConnector_registers_single_plugin()
    {
        var services = new ServiceCollection();
        services.AddImapConnector();
        var provider = services.BuildServiceProvider();
        var plugins = provider.GetServices<IProviderPlugin>().ToList();
        plugins.Should().ContainSingle().Which.Should().BeOfType<ImapProviderPlugin>();
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapProviderPlugin`. Expected FAIL: provider/plugin types do not exist.
3. - [ ] Create `src/EMaigrator.Connectors.Imap/ImapSourceProvider.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// IMAP <see cref="ISourceProvider"/> on MailKit. Reads folders and streams raw
/// RFC822 message bytes; bodies transit memory only (DESIGN.md §10).
/// </summary>
public sealed class ImapSourceProvider : ISourceProvider
{
    private readonly ConnectionDescriptor _descriptor;
    private readonly ImapConnectionSettings _settings;
    private readonly SecretBundle _secrets;
    private readonly ILogger _logger;
    private ImapClient? _client;
    private char _separator = '/';

    public ImapSourceProvider(ConnectionDescriptor descriptor, SecretBundle secrets, ILogger? logger = null)
    {
        _descriptor = descriptor;
        _settings = ImapPresets.Resolve(descriptor);
        _secrets = secrets;
        _logger = logger ?? NullLogger.Instance;
    }

    public ProviderId Id => new("imap");
    public ProviderConstraints Constraints => ImapConstraints.Default(_separator);

    private async Task<ImapClient> EnsureClientAsync(CancellationToken ct)
    {
        if (_client is { IsConnected: true, IsAuthenticated: true })
            return _client;
        _client = await ImapClientFactory.ConnectAndAuthenticateAsync(_descriptor, _settings, _secrets, _logger, ct)
            .ConfigureAwait(false);
        _separator = _client.PersonalNamespaces.Count > 0 ? _client.PersonalNamespaces[0].DirectorySeparator : '/';
        return _client;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var client = await EnsureClientAsync(ct).ConfigureAwait(false);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
            var folders = await GetAllFoldersAsync(client, ct).ConfigureAwait(false);
            var messageCount = inbox.Count;
            return new ConnectionTestResult(true, folders.Count, messageCount);
        }
        catch (ImapTransportException ex)
        {
            return new ConnectionTestResult(false, 0, 0, ex.Signature);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, 0, 0, ImapErrorNormalizer.Normalize(ex));
        }
    }

    public async Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        var folders = await GetAllFoldersAsync(client, ct).ConfigureAwait(false);
        var result = new List<CanonicalFolder>(folders.Count);
        foreach (var f in folders)
        {
            await f.StatusAsync(StatusItems.Count, ct).ConfigureAwait(false);
            result.Add(new CanonicalFolder(
                ImapFolderMapper.ToFolderPath(f.FullName, _separator),
                f.Count));
        }
        return result;
    }

    public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
        FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        var imapFolder = await client.GetFolderAsync(ImapFolderMapper.ToServerName(folder, _separator), ct)
            .ConfigureAwait(false);
        await imapFolder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        var query = BuildQuery(options);
        var uids = query is null
            ? await imapFolder.SearchAsync(SearchQuery.All, ct).ConfigureAwait(false)
            : await imapFolder.SearchAsync(query, ct).ConfigureAwait(false);

        foreach (var uid in uids)
        {
            ct.ThrowIfCancellationRequested();
            var summaries = await imapFolder.FetchAsync(
                new[] { uid },
                MessageSummaryItems.UniqueId | MessageSummaryItems.Flags |
                MessageSummaryItems.InternalDate | MessageSummaryItems.Envelope |
                MessageSummaryItems.Size, ct).ConfigureAwait(false);
            var summary = summaries.FirstOrDefault();
            if (summary is null) continue;

            yield return await BuildMessageAsync(imapFolder, summary, ct).ConfigureAwait(false);
        }
    }

    private async Task<CanonicalMessage> BuildMessageAsync(
        IMailFolder imapFolder, IMessageSummary summary, CancellationToken ct)
    {
        var env = summary.Envelope;
        var messageId = env?.MessageId;
        var from = env?.From?.ToString();
        var to = env?.To?.ToString();
        var subject = env?.Subject;
        var date = summary.InternalDate ?? env?.Date;

        // Compute the decoded-body hash for the identity key without holding the body:
        // fetch once into memory, hash decoded text, and reuse the bytes for streaming.
        var raw = await FetchRawAsync(imapFolder, summary.UniqueId, ct).ConfigureAwait(false);
        var bodyHash = ComputeDecodedBodySha256Hex(raw);

        var identityInput = ImapMessageMapper.BuildIdentityInput(
            messageId, from, to, subject, date, bodyHash);
        var identityKey = IdentityKey.Compute(identityInput);

        var uid = summary.UniqueId;
        return new CanonicalMessage
        {
            IdentityKey = identityKey,
            MessageId = messageId,
            InternalDate = (date ?? DateTimeOffset.UnixEpoch),
            Flags = ImapMessageMapper.ToCoreFlags(summary.Flags ?? MessageFlags.None),
            SizeBytes = (long)(summary.Size ?? (uint)raw.Length),
            Subject = subject,
            OpenContentAsync = async token =>
            {
                var bytes = await FetchRawAsync(imapFolder, uid, token).ConfigureAwait(false);
                return new MemoryStream(bytes, writable: false);
            },
        };
    }

    private static async Task<byte[]> FetchRawAsync(IMailFolder folder, UniqueId uid, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var mime = await folder.GetMessageAsync(uid, ct).ConfigureAwait(false);
        await mime.WriteToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static string ComputeDecodedBodySha256Hex(byte[] rawRfc822)
    {
        using var src = new MemoryStream(rawRfc822, writable: false);
        var message = MimeKit.MimeMessage.Load(src);
        var bodyText = message.TextBody ?? message.HtmlBody ?? string.Empty;
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(bodyText));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static SearchQuery? BuildQuery(ReadOptions options)
    {
        SearchQuery? q = null;
        if (options.Since is { } since)
            q = SearchQuery.DeliveredAfter(since.UtcDateTime);
        if (options.Before is { } before)
            q = q is null ? SearchQuery.DeliveredBefore(before.UtcDateTime) : q.And(SearchQuery.DeliveredBefore(before.UtcDateTime));
        return q;
    }

    private static async Task<List<IMailFolder>> GetAllFoldersAsync(ImapClient client, CancellationToken ct)
    {
        var ns = client.PersonalNamespaces.Count > 0 ? client.PersonalNamespaces[0] : null;
        var personal = ns is null ? Array.Empty<IMailFolder>() : await client.GetFoldersAsync(ns, false, ct).ConfigureAwait(false);
        var all = new List<IMailFolder> { client.Inbox };
        all.AddRange(personal.Where(f => !f.FullName.Equals(client.Inbox.FullName, StringComparison.OrdinalIgnoreCase)));
        return all;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(true).ConfigureAwait(false);
            _client.Dispose();
            _client = null;
        }
    }
}
```
   Create `src/EMaigrator.Connectors.Imap/ImapDestinationProvider.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using CoreMessage = EMaigrator.Core.Model.CanonicalMessage;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// IMAP <see cref="IDestinationProvider"/> on MailKit. Creates folder hierarchy
/// honoring the server separator and APPENDs messages preserving InternalDate and
/// flags. Idempotency is the engine's job (ledger); ExistsByMessageId supports
/// non-empty-destination dedup.
/// </summary>
public sealed class ImapDestinationProvider : IDestinationProvider
{
    private readonly ConnectionDescriptor _descriptor;
    private readonly ImapConnectionSettings _settings;
    private readonly SecretBundle _secrets;
    private readonly ILogger _logger;
    private ImapClient? _client;
    private char _separator = '/';

    public ImapDestinationProvider(ConnectionDescriptor descriptor, SecretBundle secrets, ILogger? logger = null)
    {
        _descriptor = descriptor;
        _settings = ImapPresets.Resolve(descriptor);
        _secrets = secrets;
        _logger = logger ?? NullLogger.Instance;
    }

    public ProviderId Id => new("imap");
    public ProviderConstraints Constraints => ImapConstraints.Default(_separator);

    private async Task<ImapClient> EnsureClientAsync(CancellationToken ct)
    {
        if (_client is { IsConnected: true, IsAuthenticated: true })
            return _client;
        _client = await ImapClientFactory.ConnectAndAuthenticateAsync(_descriptor, _settings, _secrets, _logger, ct)
            .ConfigureAwait(false);
        _separator = _client.PersonalNamespaces.Count > 0 ? _client.PersonalNamespaces[0].DirectorySeparator : '/';
        return _client;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var client = await EnsureClientAsync(ct).ConfigureAwait(false);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct).ConfigureAwait(false);
            // Prove append capability: APPEND a probe, then expunge it (non-destructive to user mail).
            var probe = new MimeMessage();
            probe.From.Add(new MailboxAddress("EMaigrator", _settings.AccountEmail));
            probe.To.Add(new MailboxAddress("EMaigrator", _settings.AccountEmail));
            probe.Subject = "EMaigrator connection probe";
            probe.MessageId = $"emaigrator-probe-{Guid.NewGuid():N}@emaigrator.local";
            probe.Body = new TextPart("plain") { Text = "probe" };
            var appended = await inbox.AppendAsync(probe, MessageFlags.Deleted, DateTimeOffset.UtcNow, ct)
                .ConfigureAwait(false);
            if (appended is { } uid)
            {
                await inbox.AddFlagsAsync(new[] { uid }, MessageFlags.Deleted, true, ct).ConfigureAwait(false);
                await inbox.ExpungeAsync(new[] { uid }, ct).ConfigureAwait(false);
            }
            return new ConnectionTestResult(true, 1, inbox.Count);
        }
        catch (ImapTransportException ex)
        {
            return new ConnectionTestResult(false, 0, 0, ex.Signature);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, 0, 0, ImapErrorNormalizer.Normalize(ex));
        }
    }

    public async Task EnsureFolderAsync(FolderPath folder, CancellationToken ct)
    {
        if (folder.IsRoot) return;
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        var current = client.GetFolder(client.PersonalNamespaces.Count > 0
            ? client.PersonalNamespaces[0]
            : new FolderNamespace(_separator, string.Empty));

        foreach (var segment in folder.Segments)
        {
            IMailFolder child;
            try
            {
                child = await current.GetSubfolderAsync(segment, ct).ConfigureAwait(false);
            }
            catch (FolderNotFoundException)
            {
                child = await current.CreateAsync(segment, isMessageFolder: true, ct).ConfigureAwait(false);
            }
            current = child;
        }
    }

    public async Task<WriteResult> WriteMessageAsync(FolderPath folder, CoreMessage message, CancellationToken ct)
    {
        try
        {
            var client = await EnsureClientAsync(ct).ConfigureAwait(false);
            await EnsureFolderAsync(folder, ct).ConfigureAwait(false);
            var imapFolder = await client.GetFolderAsync(ImapFolderMapper.ToServerName(folder, _separator), ct)
                .ConfigureAwait(false);

            await using var content = await message.OpenContentAsync(ct).ConfigureAwait(false);
            var mime = await MimeMessage.LoadAsync(content, ct).ConfigureAwait(false);
            var flags = ImapMessageMapper.ToMailKitFlags(message.Flags);
            var appended = await imapFolder.AppendAsync(mime, flags, message.InternalDate, ct).ConfigureAwait(false);
            return new WriteResult(true, appended?.ToString());
        }
        catch (ImapTransportException ex)
        {
            return new WriteResult(false, null, ex.Signature);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WriteResult(false, null, ImapErrorNormalizer.Normalize(ex));
        }
    }

    public async Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
    {
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        var imapFolder = await client.GetFolderAsync(ImapFolderMapper.ToServerName(folder, _separator), ct)
            .ConfigureAwait(false);
        await imapFolder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
        var trimmed = messageId.Trim('<', '>');
        var uids = await imapFolder.SearchAsync(SearchQuery.HeaderContains("Message-Id", trimmed), ct)
            .ConfigureAwait(false);
        return uids.Count > 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(true).ConfigureAwait(false);
            _client.Dispose();
            _client = null;
        }
    }
}
```
   Create `src/EMaigrator.Connectors.Imap/ImapProviderPlugin.cs`:
```csharp
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// DI-discovered IMAP plugin (CONTRACTS §2). One per connector assembly.
/// </summary>
public sealed class ImapProviderPlugin : IProviderPlugin
{
    private readonly ILoggerFactory _loggerFactory;

    public ImapProviderPlugin() : this(NullLoggerFactory.Instance) { }
    public ImapProviderPlugin(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public ProviderId Id => new("imap");

    public IReadOnlyCollection<AuthMethod> SupportedAuth { get; } =
        new[] { AuthMethod.ImapBasic, AuthMethod.ImapOAuthXoauth2 };

    public bool CanBeSource => true;
    public bool CanBeDestination => true;

    public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets)
        => new ImapSourceProvider(descriptor, secrets, _loggerFactory.CreateLogger<ImapSourceProvider>());

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
        => new ImapDestinationProvider(descriptor, secrets, _loggerFactory.CreateLogger<ImapDestinationProvider>());
}
```
   Create `src/EMaigrator.Connectors.Imap/ServiceCollectionExtensions.cs`:
```csharp
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Connectors.Imap;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the IMAP connector plugin (CONTRACTS §8 naming convention).</summary>
    public static IServiceCollection AddImapConnector(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderPlugin, ImapProviderPlugin>());
        return services;
    }
}
```
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapProviderPlugin`. Expected PASS: all 4 cases green (compilation proves the interface conformance).
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap/ImapSourceProvider.cs src/EMaigrator.Connectors.Imap/ImapDestinationProvider.cs src/EMaigrator.Connectors.Imap/ImapProviderPlugin.cs src/EMaigrator.Connectors.Imap/ServiceCollectionExtensions.cs src/EMaigrator.Connectors.Imap.Tests/ImapProviderPluginTests.cs
git commit -m "feat(imap): implement IMAP source/destination providers and DI-discovered plugin

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: GreenMail Testcontainers fixture + source read contract test

**Goal:** Stand up a real containerized IMAP server (GreenMail) via Testcontainers, seed it, and prove `ImapSourceProvider` reads concrete folder + message counts and streams raw bytes with a correct `IdentityKey`, `InternalDate`, and flags.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap.IntegrationTests/GreenMailImapFixture.cs`
- Create: `src/EMaigrator.Connectors.Imap.IntegrationTests/EMaigrator.Connectors.Imap.IntegrationTests.csproj`
- Create: `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapSourceReadTests.cs`

**Acceptance Criteria:**
- [ ] The fixture starts `greenmail/standalone:2.1.0`, exposing IMAP port `3143` (plaintext, test-only — exercised with explicit `allowPlaintext=true`), and creates user `migrator@local.test` / password `pw`.
- [ ] `TestConnectionAsync` returns `Ok=true` with `FolderCount >= 1` and `MessageCount` equal to the number of seeded INBOX messages.
- [ ] `ListFoldersAsync` returns a `CanonicalFolder` for `INBOX` and the seeded sub-folder `Projects` (mapped to `FolderPath` `Projects` or `INBOX/Projects` per server layout) with `EstimatedMessageCount` matching seeded counts.
- [ ] `ReadMessagesAsync(INBOX)` yields the seeded message with non-null `IdentityKey` starting `mid:` (Message-ID present), `InternalDate` equal to the appended date (±1s), `Flags` including `Seen`, and `OpenContentAsync` stream that parses to a `MimeMessage` whose Subject matches.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapSourceRead` → all pass (requires Docker).

**Steps:**
1. - [ ] Create the integration test project `src/EMaigrator.Connectors.Imap.IntegrationTests/EMaigrator.Connectors.Imap.IntegrationTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <LangVersion>13</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="Testcontainers" Version="3.10.0" />
    <PackageReference Include="MailKit" Version="4.8.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Connectors.Imap\EMaigrator.Connectors.Imap.csproj" />
    <ProjectReference Include="..\EMaigrator.Core\EMaigrator.Core.csproj" />
  </ItemGroup>
</Project>
```
   Then write the failing fixture + test. Create `src/EMaigrator.Connectors.Imap.IntegrationTests/GreenMailImapFixture.cs`:
```csharp
using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MimeKit;
using Xunit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

/// <summary>
/// A real GreenMail IMAP+SMTP server in a container. Plaintext ports are used —
/// this is test-only and the connector is exercised with explicit allowPlaintext.
/// </summary>
public sealed class GreenMailImapFixture : IAsyncLifetime
{
    public const string UserEmail = "migrator@local.test";
    public const string UserName = "migrator";
    public const string Password = "pw";

    private IContainer _container = null!;
    public string Host { get; private set; } = "127.0.0.1";
    public int ImapPort { get; private set; }
    public int SmtpPort { get; private set; }

    public async Task InitializeAsync()
    {
        // NOTE: auth is ENFORCED (no -Dgreenmail.auth.disabled). This is deliberate:
        // the Task 11 security gate must be able to provoke a REAL authentication
        // failure (wrong password -> imap:auth-failed). With auth disabled GreenMail
        // accepts any credentials and the credential-leak-on-failure check could never
        // fire — exactly the "cheaper substitute check" the user-gate forbids.
        // -Dgreenmail.setup.test.all auto-provisions a mailbox on first successful
        // login; SeedUserAsync below creates migrator@local.test at the known password.
        _container = new ContainerBuilder()
            .WithImage("greenmail/standalone:2.1.0")
            .WithEnvironment("GREENMAIL_OPTS",
                "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.verbose")
            .WithPortBinding(3143, true)
            .WithPortBinding(3025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3143))
            .Build();
        await _container.StartAsync();
        ImapPort = _container.GetMappedPublicPort(3143);
        SmtpPort = _container.GetMappedPublicPort(3025);
        await SeedUserAsync();
    }

    /// <summary>
    /// Provisions migrator@local.test at <see cref="Password"/> by performing one
    /// authenticated IMAP login (greenmail.setup.test.all auto-creates the account at
    /// the password first used). After this, a login with any OTHER password fails
    /// deterministically — which the Task 11 security gate relies on.
    /// </summary>
    public async Task SeedUserAsync()
    {
        using var client = new ImapClient();
        await client.ConnectAsync(Host, ImapPort, MailKit.Security.SecureSocketOptions.None);
        await client.AuthenticateAsync(UserEmail, Password);
        await client.DisconnectAsync(true);
    }

    /// <summary>Deliver a message to the user via SMTP so it lands in INBOX.</summary>
    public async Task DeliverToInboxAsync(string subject, string body, string messageId)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@local.test"));
        msg.To.Add(new MailboxAddress("Migrator", UserEmail));
        msg.Subject = subject;
        msg.MessageId = messageId;
        msg.Body = new TextPart("plain") { Text = body };
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(Host, SmtpPort, MailKit.Security.SecureSocketOptions.None);
        await smtp.SendAsync(msg);
        await smtp.DisconnectAsync(true);
    }

    /// <summary>APPEND directly into a (possibly new) folder, preserving flags+date.</summary>
    public async Task AppendAsync(string folderName, string subject, string body, string messageId,
        MailKit.MessageFlags flags, DateTimeOffset date)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(Host, ImapPort, MailKit.Security.SecureSocketOptions.None);
        await client.AuthenticateAsync(UserEmail, Password);
        var folder = client.Inbox;
        if (!folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            try { folder = await client.Inbox.GetSubfolderAsync(folderName); }
            catch (FolderNotFoundException) { folder = await client.Inbox.CreateAsync(folderName, true); }
        }
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@local.test"));
        msg.To.Add(new MailboxAddress("Migrator", UserEmail));
        msg.Subject = subject;
        msg.MessageId = messageId;
        msg.Body = new TextPart("plain") { Text = body };
        await folder.OpenAsync(MailKit.FolderAccess.ReadWrite);
        await folder.AppendAsync(msg, flags, date);
        await client.DisconnectAsync(true);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("greenmail")]
public sealed class GreenMailCollection : ICollectionFixture<GreenMailImapFixture> { }
```
   Create `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapSourceReadTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using MimeKit;
using Xunit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

[Collection("greenmail")]
public class ImapSourceReadTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapSourceReadTests(GreenMailImapFixture fx) => _fx = fx;

    private ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = _fx.Host,
            ["port"] = _fx.ImapPort.ToString(),
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };

    private SecretBundle Secret() =>
        new(new Dictionary<string, string> { ["password"] = GreenMailImapFixture.Password });

    [Fact]
    public async Task TestConnection_reports_folder_and_message_counts()
    {
        var mid = $"<read-conn-{Guid.NewGuid():N}@local.test>";
        await _fx.DeliverToInboxAsync("conn-test", "hi", mid);

        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        var result = await src.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.FolderCount.Should().BeGreaterThanOrEqualTo(1);
        result.MessageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ReadMessages_yields_canonical_message_with_identity_date_flags_and_stream()
    {
        var subject = $"subj-{Guid.NewGuid():N}";
        var mid = $"<read-msg-{Guid.NewGuid():N}@local.test>";
        var date = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        await _fx.AppendAsync("INBOX", subject, "body-text", mid, MailKit.MessageFlags.Seen, date);

        await using var src = new ImapSourceProvider(Descriptor(), Secret());

        CanonicalMessage? found = null;
        await foreach (var m in src.ReadMessagesAsync(FolderPath.Parse("INBOX"), new ReadOptions(), CancellationToken.None))
        {
            if (m.Subject == subject) { found = m; break; }
        }

        found.Should().NotBeNull();
        found!.IdentityKey.Should().StartWith("mid:");
        found.InternalDate.Should().BeCloseTo(date, TimeSpan.FromSeconds(2));
        found.Flags.Should().HaveFlag(MessageFlags.Seen);

        await using var stream = await found.OpenContentAsync(CancellationToken.None);
        var parsed = await MimeMessage.LoadAsync(stream);
        parsed.Subject.Should().Be(subject);
    }

    [Fact]
    public async Task ListFolders_includes_inbox_and_created_subfolder()
    {
        await _fx.AppendAsync("Projects", "p1", "b", $"<proj-{Guid.NewGuid():N}@local.test>",
            MailKit.MessageFlags.None, DateTimeOffset.UtcNow);

        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        var folders = await src.ListFoldersAsync(CancellationToken.None);

        folders.Select(f => f.Path.Name).Should().Contain(new[] { "INBOX", "Projects" });
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapSourceRead`. Expected FAIL initially: fixture/test compile but the run should be executed AFTER Task 7's providers exist. Since Task 7 is a hard dependency, the expected first-run failure here is a behavioral assertion failure if any mapping is wrong (e.g. `InternalDate` off, or folder name mapping). If providers were incomplete it would be a compile error. Capture the actual failing assertion.
3. - [ ] Make the minimal fixes in `src/EMaigrator.Connectors.Imap/ImapSourceProvider.cs` if the run reveals a mapping defect (e.g. GreenMail reports the separator as `'.'` — adjust `_separator` derivation; or `ListFoldersAsync` must include nested folders — ensure `GetFoldersAsync(ns, subscribedOnly:false)` recurses). Apply only the change the failing assertion demands. No code is rewritten speculatively.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapSourceRead`. Expected PASS: all 3 cases green against the live container.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap.IntegrationTests/EMaigrator.Connectors.Imap.IntegrationTests.csproj src/EMaigrator.Connectors.Imap.IntegrationTests/GreenMailImapFixture.cs src/EMaigrator.Connectors.Imap.IntegrationTests/ImapSourceReadTests.cs
git commit -m "test(imap): GreenMail Testcontainers fixture + source read contract tests

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: Destination append + EnsureFolder hierarchy + ExistsByMessageId contract tests

**Goal:** Prove against the live GreenMail server that `ImapDestinationProvider` creates nested folders honoring the server separator, APPENDs a `CanonicalMessage` preserving `InternalDate` + flags, and that `ExistsByMessageIdAsync` finds it via `SEARCH HEADER Message-ID`.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapDestinationWriteTests.cs`

**Acceptance Criteria:**
- [ ] `EnsureFolderAsync(FolderPath["Archive","2026","Q1"])` creates the full nested hierarchy; a subsequent IMAP LIST confirms the leaf exists.
- [ ] `WriteMessageAsync` into that folder returns `WriteResult.Written = true`; reading the folder back shows the message with the same Subject, the same `Seen|Flagged` flags, and `InternalDate` within ±2s of the supplied date.
- [ ] `ExistsByMessageIdAsync(folder, "<the-message-id>")` returns `true` after the write and `false` for an absent Message-ID.
- [ ] `EnsureFolderAsync` is idempotent: calling it twice does not throw and does not create duplicates.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapDestinationWrite` → all pass (requires Docker).

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapDestinationWriteTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using MimeKit;
using Xunit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

[Collection("greenmail")]
public class ImapDestinationWriteTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapDestinationWriteTests(GreenMailImapFixture fx) => _fx = fx;

    private ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = _fx.Host,
            ["port"] = _fx.ImapPort.ToString(),
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };

    private SecretBundle Secret() =>
        new(new Dictionary<string, string> { ["password"] = GreenMailImapFixture.Password });

    private static CanonicalMessage BuildMessage(string subject, string messageId, DateTimeOffset date, MessageFlags flags)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("S", "s@local.test"));
        mime.To.Add(new MailboxAddress("D", GreenMailImapFixture.UserEmail));
        mime.Subject = subject;
        mime.MessageId = messageId.Trim('<', '>');
        mime.Body = new TextPart("plain") { Text = "destination body" };
        var ms = new MemoryStream();
        mime.WriteTo(ms);
        var bytes = ms.ToArray();
        return new CanonicalMessage
        {
            IdentityKey = "mid:" + messageId,
            MessageId = messageId,
            InternalDate = date,
            Flags = flags,
            Subject = subject,
            SizeBytes = bytes.Length,
            OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
        };
    }

    [Fact]
    public async Task EnsureFolder_creates_nested_hierarchy_idempotently()
    {
        var path = new FolderPath(new[] { "Archive", "2026", "Q1" });
        await using var dst = new ImapDestinationProvider(Descriptor(), Secret());

        await dst.EnsureFolderAsync(path, CancellationToken.None);
        await dst.EnsureFolderAsync(path, CancellationToken.None); // idempotent

        // ExistsByMessageId opens the leaf folder — succeeds only if it was created.
        var act = async () => await dst.ExistsByMessageIdAsync(path, "<none@local.test>", CancellationToken.None);
        (await act.Should().NotThrowAsync()).Subject.Should().BeFalse();
    }

    [Fact]
    public async Task WriteMessage_appends_preserving_date_and_flags_and_is_searchable()
    {
        var path = new FolderPath(new[] { "Migrated" });
        var subject = $"dst-{Guid.NewGuid():N}";
        var mid = $"<dst-{Guid.NewGuid():N}@local.test>";
        var date = new DateTimeOffset(2025, 12, 24, 18, 30, 0, TimeSpan.Zero);
        var msg = BuildMessage(subject, mid, date, MessageFlags.Seen | MessageFlags.Flagged);

        await using var dst = new ImapDestinationProvider(Descriptor(), Secret());
        var write = await dst.WriteMessageAsync(path, msg, CancellationToken.None);
        write.Written.Should().BeTrue();

        (await dst.ExistsByMessageIdAsync(path, mid, CancellationToken.None)).Should().BeTrue();
        (await dst.ExistsByMessageIdAsync(path, "<absent@local.test>", CancellationToken.None)).Should().BeFalse();

        // Read it back through the source provider to confirm date+flags survived.
        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        CanonicalMessage? roundtrip = null;
        await foreach (var m in src.ReadMessagesAsync(path, new ReadOptions(), CancellationToken.None))
        {
            if (m.Subject == subject) { roundtrip = m; break; }
        }
        roundtrip.Should().NotBeNull();
        roundtrip!.InternalDate.Should().BeCloseTo(date, TimeSpan.FromSeconds(2));
        roundtrip.Flags.Should().HaveFlag(MessageFlags.Seen);
        roundtrip.Flags.Should().HaveFlag(MessageFlags.Flagged);
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapDestinationWrite`. Expected FAIL on first run if any append/search/separator detail is wrong (capture the actual assertion, e.g. `ExistsByMessageId` false because GreenMail stores Message-Id with angle brackets — adjust the `Trim('<','>')`/search term).
3. - [ ] Apply the minimal fix in `src/EMaigrator.Connectors.Imap/ImapDestinationProvider.cs` that the failing assertion demands (e.g. switch `SearchQuery.HeaderContains("Message-Id", trimmed)` term handling, or ensure `GetFolderAsync` uses the server-reported separator captured at connect). Change only what the red test requires.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapDestinationWrite`. Expected PASS: both cases green.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap.IntegrationTests/ImapDestinationWriteTests.cs src/EMaigrator.Connectors.Imap/ImapDestinationProvider.cs
git commit -m "test(imap): destination append + nested EnsureFolder + ExistsByMessageId contract tests

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: Functional Verification — end-to-end source→destination roundtrip + idempotent re-append

**Goal:** Prove the subsystem's headline behavior end-to-end: read every message of a multi-folder source mailbox through `ImapSourceProvider`, write each into a destination folder tree through `ImapDestinationProvider`, and confirm a re-run (driven by the engine's `ExistsByMessageIdAsync` dedup) adds **zero** duplicates — the non-destructive, idempotent contract from DESIGN.md §6.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapRoundtripFunctionalTests.cs`

**Acceptance Criteria:**
- [ ] Seed a source with N=5 INBOX messages + 3 in a `Projects` subfolder (distinct Message-IDs, mixed flags, distinct dates).
- [ ] A copy loop reads all source folders and APPENDs each message into a destination tree (`Migrated/<sourceFolder>`); the destination's per-folder message counts equal the source's.
- [ ] Every destination message has the same Subject, flags, and `InternalDate` (±2s) as its source counterpart.
- [ ] A second copy pass that skips a message when `ExistsByMessageIdAsync` returns true results in destination counts **unchanged** (idempotent re-append: no duplicates).
- [ ] `IdentityKey` values are stable across the two source reads of the same message (re-reading yields the identical key).

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapRoundtripFunctional` → all pass (requires Docker).

**Steps:**
1. - [ ] Write the failing test `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapRoundtripFunctionalTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

[Collection("greenmail")]
public class ImapRoundtripFunctionalTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapRoundtripFunctionalTests(GreenMailImapFixture fx) => _fx = fx;

    private ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = _fx.Host,
            ["port"] = _fx.ImapPort.ToString(),
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };
    private SecretBundle Secret() => new(new Dictionary<string, string> { ["password"] = GreenMailImapFixture.Password });

    private async Task<long> CopyAsync(bool dedup)
    {
        long written = 0;
        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        await using var dst = new ImapDestinationProvider(Descriptor(), Secret());

        var folders = await src.ListFoldersAsync(CancellationToken.None);
        foreach (var folder in folders.Where(f => f.Path.Name is "INBOX" or "FuncProjects"))
        {
            var destPath = new FolderPath(new[] { "Migrated", folder.Path.Name });
            await dst.EnsureFolderAsync(destPath, CancellationToken.None);
            await foreach (var msg in src.ReadMessagesAsync(folder.Path, new ReadOptions(), CancellationToken.None))
            {
                if (dedup && msg.MessageId is not null &&
                    await dst.ExistsByMessageIdAsync(destPath, msg.MessageId, CancellationToken.None))
                    continue;
                var r = await dst.WriteMessageAsync(destPath, msg, CancellationToken.None);
                if (r.Written) written++;
            }
        }
        return written;
    }

    private async Task<long> CountAsync(string destFolderName)
    {
        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        var folders = await src.ListFoldersAsync(CancellationToken.None);
        var folder = folders.FirstOrDefault(f => f.Path.Name == destFolderName);
        return folder?.EstimatedMessageCount ?? 0;
    }

    [Fact]
    public async Task Roundtrip_copies_all_messages_then_reruns_with_zero_duplicates()
    {
        var run = Guid.NewGuid().ToString("N").Substring(0, 6);
        for (var i = 0; i < 5; i++)
            await _fx.AppendAsync("INBOX", $"inbox-{run}-{i}", "b", $"<ib-{run}-{i}@local.test>",
                MailKit.MessageFlags.Seen, DateTimeOffset.UtcNow.AddDays(-i));
        for (var i = 0; i < 3; i++)
            await _fx.AppendAsync("FuncProjects", $"proj-{run}-{i}", "b", $"<pj-{run}-{i}@local.test>",
                MailKit.MessageFlags.Flagged, DateTimeOffset.UtcNow.AddDays(-i));

        // First copy pass (no dedup): everything is written.
        var firstWritten = await CopyAsync(dedup: false);
        firstWritten.Should().BeGreaterThanOrEqualTo(8);

        var inboxCountAfterFirst = await CountAsync("INBOX");   // dest leaf "INBOX" under Migrated
        var projCountAfterFirst = await CountAsync("FuncProjects");
        inboxCountAfterFirst.Should().BeGreaterThanOrEqualTo(5);
        projCountAfterFirst.Should().BeGreaterThanOrEqualTo(3);

        // Second pass WITH dedup: ExistsByMessageId short-circuits each → zero writes.
        var secondWritten = await CopyAsync(dedup: true);
        secondWritten.Should().Be(0);

        (await CountAsync("INBOX")).Should().Be(inboxCountAfterFirst);
        (await CountAsync("FuncProjects")).Should().Be(projCountAfterFirst);
    }

    [Fact]
    public async Task IdentityKey_is_stable_across_reads()
    {
        var mid = $"<stable-{Guid.NewGuid():N}@local.test>";
        await _fx.AppendAsync("INBOX", $"stable-{mid}", "b", mid, MailKit.MessageFlags.None, DateTimeOffset.UtcNow);

        async Task<string?> ReadKey()
        {
            await using var src = new ImapSourceProvider(Descriptor(), Secret());
            await foreach (var m in src.ReadMessagesAsync(FolderPath.Parse("INBOX"), new ReadOptions(), CancellationToken.None))
                if (m.MessageId == mid) return m.IdentityKey;
            return null;
        }

        var k1 = await ReadKey();
        var k2 = await ReadKey();
        k1.Should().NotBeNull();
        k1.Should().Be(k2);
        k1!.Should().StartWith("mid:");
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapRoundtripFunctional`. Expected FAIL on first run if the dest-folder naming/count lookup or dedup search has a defect (e.g. the dest leaf folder count is looked up by `Path.Name` but the source `ListFolders` returns `Migrated/INBOX` — capture the actual assertion).
3. - [ ] Apply the minimal fix the failing assertion demands. If the count-by-leaf-name is ambiguous because both source `INBOX` and dest `Migrated/INBOX` share a leaf name, adjust the test's `CountAsync` to match on the full `Path.ToString()` (e.g. `f.Path.ToString() == "Migrated/INBOX"`), OR fix a real provider mapping bug (separator handling in `GetFolderAsync`). Prefer the provider fix when the defect is in the connector; adjust the test only when the test's lookup is the inaccuracy. Document which in the commit body.
4. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapRoundtripFunctional`. Expected PASS: both cases green — full roundtrip with zero duplicates on re-run.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap.IntegrationTests/ImapRoundtripFunctionalTests.cs
git commit -m "test(imap): end-to-end roundtrip + idempotent re-append functional verification

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: Security Verification — credentials never logged, TLS enforced, no SSRF (USER-GATE)

**Goal:** Prove this connector's security focus: credentials never appear in logs or exception messages, TLS is enforced on the wire (plaintext rejected unless explicitly opted in), and the test-connection target host is validated against the configured preset/custom allowlist (no SSRF to an arbitrary host).

**USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Connectors.Imap.Tests/ImapSecurityVerificationTests.cs`
- Create: `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapSecurityLiveTests.cs`

**Acceptance Criteria:**
- [ ] **Auth is enforced (no cheaper substitute):** the GreenMail fixture runs WITHOUT `-Dgreenmail.auth.disabled` and seeds `migrator@local.test` at the known password via `SeedUserAsync`, so a wrong password produces a REAL `imap:auth-failed` — the credential-leak-on-failure check cannot be no-opped.
- [ ] **Credential never in logs:** with a `CapturingLogger` injected, a wrong-password authentication attempt against the live GreenMail server produces zero log entries whose text contains either the attempted password or the seeded password substring (assert over every captured message, exception text, and scope state). Captured-log assertion output is shown.
- [ ] **Credential never in exception messages:** the wrong-password failure (driven directly through `ImapClientFactory.ConnectAndAuthenticateAsync`) surfaces an `ImapTransportException` whose `Message` AND `Signature` are exactly `imap:auth-failed` and contain neither the password nor the username; and `TestConnectionAsync` returns `ConnectionTestResult` with `ErrorCode == "imap:auth-failed"` and `RawDetail == null` (no credential-bearing raw detail). These assertions run unconditionally — no `if (!Ok)` guard.
- [ ] **TLS enforced:** a `ConnectionDescriptor` for a custom host with `useSsl=false` and NO `allowPlaintext` causes `ImapPresets.Resolve` (invoked in the provider constructor) to throw `ImapConfigurationException` mentioning "TLS"; the provider never opens a socket (asserted: `CapturingLogger` has no "Connecting to IMAP host" entry).
- [ ] **No SSRF:** a WorkMail-preset descriptor whose `region=eu-west-1` but with a planted `host=169.254.169.254` setting still resolves to `imap.mail.eu-west-1.awsapps.com` (the planted host is ignored for presets); and `ImapHostValidator.Validate` rejects any attempt to dial a host other than the preset host — proven by a test that constructs a custom descriptor pointing at `169.254.169.254` and asserts `TestConnectionAsync` returns `Ok == false` with an `imap:` `ErrorCode` and never connects (no "Connecting to IMAP host 169.254.169.254" log entry).
- [ ] **grep proof:** a repository grep over the connector source shows zero string-interpolation of `password`/`accessToken`/`secrets.Values` into any `ILogger` call (`Grep` pattern `Log\w*\([^)]*(password|accessToken|secrets)` over path `src/EMaigrator.Connectors.Imap` returns **0 matches**); captured (empty) grep output is shown.

**Verify:** `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapSecurityVerification` `&&` `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapSecurityLive` → all pass.

**Steps:**
1. - [ ] Write the failing unit test `src/EMaigrator.Connectors.Imap.Tests/ImapSecurityVerificationTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapSecurityVerificationTests
{
    [Fact]
    public void Tls_is_enforced_when_plaintext_not_opted_in()
    {
        var d = new ConnectionDescriptor
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string>
            {
                ["preset"] = "custom",
                ["host"] = "mail.example.org",
                ["port"] = "143",
                ["useSsl"] = "false",
                ["accountEmail"] = "u@example.org",
            },
        };
        var act = () => ImapPresets.Resolve(d);
        act.Should().Throw<ImapConfigurationException>().Which.Message.Should().Contain("TLS");
    }

    [Fact]
    public void Workmail_preset_ignores_planted_host_and_resolves_to_region_host()
    {
        var d = new ConnectionDescriptor
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string>
            {
                ["preset"] = "workmail",
                ["region"] = "eu-west-1",
                ["host"] = "169.254.169.254", // planted; must be ignored for presets
                ["accountEmail"] = "u@corp.example",
            },
        };
        var settings = ImapPresets.Resolve(d);
        settings.Host.Should().Be("imap.mail.eu-west-1.awsapps.com");

        // And the validator forbids dialing anything but the preset host.
        var act = () => ImapHostValidator.Validate(d, "169.254.169.254");
        act.Should().Throw<ImapConfigurationException>();
    }

    [Fact]
    public void Error_normalizer_signature_carries_no_credential()
    {
        const string pw = "Sup3rSecret-PW-XYZ";
        var ex = new MailKit.Security.AuthenticationException($"login failed for {pw}");
        var sig = ImapErrorNormalizer.Normalize(ex);
        sig.Should().Be("imap:auth-failed");
        sig.Should().NotContain(pw);
    }
}
```
2. - [ ] Run `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapSecurityVerification`. This is the RED probe for the security invariants. Expected: if a real gap exists (e.g. `Resolve` does not enforce TLS, or the normalizer leaks the credential) the test FAILS — capture the failing assertion and fix it minimally in the relevant Task 1–4 file (e.g. add the `"TLS"` guard in `ImapPresets.Resolve`, or remove a message-bearing branch in `ImapErrorNormalizer`). If the invariants already hold the probe is green; capture the output either way.
3. - [ ] Write the failing live security test `src/EMaigrator.Connectors.Imap.IntegrationTests/ImapSecurityLiveTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

/// <summary>Captures every log line + scope value for credential-leak assertions.</summary>
public sealed class CapturingLogger : ILogger
{
    public readonly List<string> Lines = new();
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    { Lines.Add($"scope:{state}"); return NullScope.Instance; }
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
        Func<TState, Exception?, string> formatter)
    {
        Lines.Add(formatter(state, ex));
        if (ex is not null) Lines.Add(ex.ToString());
    }
    private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
}

[Collection("greenmail")]
public class ImapSecurityLiveTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapSecurityLiveTests(GreenMailImapFixture fx) => _fx = fx;

    private ConnectionDescriptor CustomDescriptor(string host, int port, bool allowPlaintext = true) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = host,
            ["port"] = port.ToString(),
            ["useSsl"] = "false",
            ["allowPlaintext"] = allowPlaintext ? "true" : "false",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };

    [Fact]
    public async Task Wrong_password_yields_auth_failed_signature_with_no_credential_in_logs()
    {
        // Auth is ENFORCED on the fixture and migrator@local.test was seeded at the
        // known Password (see GreenMailImapFixture.SeedUserAsync). A DIFFERENT password
        // therefore deterministically fails authentication.
        const string wrongPassword = "Sup3rSecret-PW-XYZ-WRONG";
        var logger = new CapturingLogger();
        var d = CustomDescriptor(_fx.Host, _fx.ImapPort);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = wrongPassword });

        await using var src = new ImapSourceProvider(d, secret, logger);
        var bad = await src.TestConnectionAsync(CancellationToken.None);

        // 1. The auth failure surfaces as the stable, credential-free signature.
        bad.Ok.Should().BeFalse();
        bad.ErrorCode.Should().Be("imap:auth-failed");
        // 2. No raw, credential-bearing detail is propagated.
        (bad.RawDetail ?? string.Empty).Should().NotContain(wrongPassword);
        bad.RawDetail.Should().BeNull();
        // 3. No captured log line — message, exception text, or scope — contains the secret.
        logger.Lines.Should().NotContain(l => l.Contains(wrongPassword));
        logger.Lines.Should().NotContain(l => l.Contains(GreenMailImapFixture.Password));
    }

    [Fact]
    public async Task Auth_failure_exception_message_carries_only_the_signature()
    {
        // Drives ImapClientFactory directly to assert the wrapped transport exception's
        // Message/Signature is EXACTLY the credential-free signature — never the raw
        // MailKit message that may echo the attempted credential.
        const string wrongPassword = "Another-Wrong-PW-9999";
        var logger = new CapturingLogger();
        var d = CustomDescriptor(_fx.Host, _fx.ImapPort);
        var settings = ImapPresets.Resolve(d);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = wrongPassword });

        var act = async () => await ImapClientFactory.ConnectAndAuthenticateAsync(
            d, settings, secret, logger, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ImapTransportException>()).Which;
        ex.Signature.Should().Be("imap:auth-failed");
        ex.Message.Should().Be("imap:auth-failed");
        ex.Message.Should().NotContain(wrongPassword);
        logger.Lines.Should().NotContain(l => l.Contains(wrongPassword));
    }

    [Fact]
    public async Task Ssrf_attempt_to_metadata_host_never_opens_socket()
    {
        var logger = new CapturingLogger();
        // Custom descriptor declaring the metadata host without plaintext opt-in:
        var d = CustomDescriptor("169.254.169.254", 993, allowPlaintext: false);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = "x" });

        await using var src = new ImapSourceProvider(d, secret, logger);
        var result = await src.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeFalse();
        // The validator must have blocked it BEFORE any connect log line was emitted.
        logger.Lines.Should().NotContain(l => l.Contains("Connecting to IMAP host 169.254.169.254"));
    }

    [Fact]
    public async Task Plaintext_without_optin_is_rejected_before_socket()
    {
        var logger = new CapturingLogger();
        var d = CustomDescriptor(_fx.Host, _fx.ImapPort, allowPlaintext: false);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = "x" });

        // Resolve happens in the provider ctor → expect a configuration exception.
        var act = () => new ImapSourceProvider(d, secret, logger);
        act.Should().Throw<ImapConfigurationException>().Which.Message.Should().Contain("TLS");
        logger.Lines.Should().NotContain(l => l.Contains("Connecting to IMAP host"));
    }
}
```
4. - [ ] Run both:
   - `dotnet test src/EMaigrator.Connectors.Imap.Tests --filter FullyQualifiedName~ImapSecurityVerification`
   - `dotnet test src/EMaigrator.Connectors.Imap.IntegrationTests --filter FullyQualifiedName~ImapSecurityLive`
   Expected: any failure marks a real leak/SSRF gap; fix it minimally in the connector (e.g. ensure the "Connecting to IMAP host" log is emitted ONLY after `ImapHostValidator.Validate` passes; ensure no `catch` re-throws the original credential-bearing exception). Then re-run until PASS. Independently run the grep proof and capture its (empty) output:
   - `Grep` pattern `Log\w*\([^)]*(password|accessToken|secrets)` over path `src/EMaigrator.Connectors.Imap` → expect **0 matches**.
5. - [ ] Commit:
```
git add src/EMaigrator.Connectors.Imap.Tests/ImapSecurityVerificationTests.cs src/EMaigrator.Connectors.Imap.IntegrationTests/ImapSecurityLiveTests.cs
git commit -m "test(imap): security verification — no credential logging, TLS enforced, anti-SSRF host validation

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
