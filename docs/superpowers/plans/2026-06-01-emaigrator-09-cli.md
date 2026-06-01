# EMaigrator.Cli Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Build `EMaigrator.Cli` — a System.CommandLine console app that wraps the migration engine for self-host / headless use, exposing `migration new`, `connect test`, `preflight`, `run`, `resume`, `status`, and `report` commands, composing Core + Infrastructure + connectors + `IJobOrchestrator` via DI (with an in-process single-node worker), reading credentials only from environment variables or a secure no-echo prompt, persisting them through `ISecretStore`, emitting both human and `--json` output, and returning precise per-failure-class exit codes.

**Architecture:** The CLI is a *composition root* — it references `EMaigrator.Core` (abstractions/model only), `EMaigrator.Infrastructure` (EF/Postgres ledger, `ISecretStore`, MassTransit, Redis), and the three connector assemblies, wiring them through `Microsoft.Extensions.Hosting` DI exactly as the Api/Workers do; the engine logic lives entirely behind the frozen interfaces and is never re-implemented here. A profile file (a non-secret JSON document describing endpoints + scope) plus `appsettings.json` configure a run; secrets are resolved at runtime from env/prompt → `ISecretStore` → `SecretRef` on the `ConnectionDescriptor`. For self-host single-node the CLI starts an in-process `IJobOrchestrator` + worker so a `run` completes without a separate worker deployment.

**Tech Stack:** C#/.NET 10 (LTS); `System.CommandLine` (2.0); `Microsoft.Extensions.Hosting` / `…DependencyInjection` / `…Configuration`; `Spectre.Console` for human-formatted tables/progress; `System.Text.Json` for `--json`. Tests: xUnit, FluentAssertions, NSubstitute (command-parsing + output units), Testcontainers (Postgres + RabbitMQ + Redis + GreenMail IMAP) for the preflight/run/resume integration tests.

---

### Task 1: CLI project scaffold + root command + global options

**Goal:** Create the `EMaigrator.Cli` project with a `System.CommandLine` root command, the global `--profile`, `--json`, and `--verbose` options, and a typed `CliExitCode` enum, wired so `EMaigrator --help` runs and returns 0.

**Files:**
- Create: `src/EMaigrator.Cli/EMaigrator.Cli.csproj`
- Create: `src/EMaigrator.Cli/Program.cs`
- Create: `src/EMaigrator.Cli/CliExitCode.cs`
- Create: `src/EMaigrator.Cli/GlobalOptions.cs`
- Create: `src/EMaigrator.Cli.Tests/EMaigrator.Cli.Tests.csproj`
- Create: `src/EMaigrator.Cli.Tests/RootCommandTests.cs`
- Test: `src/EMaigrator.Cli.Tests/RootCommandTests.cs`

**Acceptance Criteria:**
- [ ] `EMaigrator.Cli.csproj` targets `net10.0`, references `EMaigrator.Core`, `EMaigrator.Infrastructure`, `EMaigrator.Connectors.Imap`, `EMaigrator.Connectors.Graph`, `EMaigrator.Connectors.Gmail`, and the `System.CommandLine`, `Microsoft.Extensions.Hosting`, `Spectre.Console` packages.
- [ ] `CliExitCode` enum defines exactly: `Success=0`, `UsageError=2`, `ConnectionFailed=3`, `PreflightBlocked=4`, `MigrationFailed=5`, `MigrationPartial=6`, `ConfigError=7`, `Cancelled=130`.
- [ ] The root command name is `emaigrator`, has a description, and registers global `--profile <path>`, `--json`, and `--verbose` options.
- [ ] Invoking with `--help` writes usage to stdout and exits `Success` (0).
- [ ] The dependency rule holds: the project references no project that itself references `EMaigrator.Cli` (no cycles).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~RootCommandTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/RootCommandTests.cs`:
```csharp
using System.CommandLine;
using EMaigrator.Cli;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests;

public class RootCommandTests
{
    [Fact]
    public void Root_command_is_named_emaigrator_and_has_global_options()
    {
        RootCommand root = CommandFactory.BuildRootCommand();

        root.Name.Should().Be("emaigrator");
        root.Options.Should().Contain(o => o.Name == "--profile");
        root.Options.Should().Contain(o => o.Name == "--json");
        root.Options.Should().Contain(o => o.Name == "--verbose");
    }

    [Fact]
    public void Help_invocation_returns_success_exit_code()
    {
        RootCommand root = CommandFactory.BuildRootCommand();

        int exit = root.Parse("--help").Invoke();

        exit.Should().Be((int)CliExitCode.Success);
    }

    [Fact]
    public void Exit_codes_have_the_frozen_numeric_values()
    {
        ((int)CliExitCode.Success).Should().Be(0);
        ((int)CliExitCode.UsageError).Should().Be(2);
        ((int)CliExitCode.ConnectionFailed).Should().Be(3);
        ((int)CliExitCode.PreflightBlocked).Should().Be(4);
        ((int)CliExitCode.MigrationFailed).Should().Be(5);
        ((int)CliExitCode.MigrationPartial).Should().Be(6);
        ((int)CliExitCode.ConfigError).Should().Be(7);
        ((int)CliExitCode.Cancelled).Should().Be(130);
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~RootCommandTests` → expected **FAIL**: `EMaigrator.Cli` / `CommandFactory` / `CliExitCode` do not exist (compile error).

3. - [ ] Create `src/EMaigrator.Cli/EMaigrator.Cli.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>emaigrator</AssemblyName>
    <RootNamespace>EMaigrator.Cli</RootNamespace>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta5.25306.1" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
    <PackageReference Include="Spectre.Console" Version="0.49.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Core\EMaigrator.Core.csproj" />
    <ProjectReference Include="..\EMaigrator.Infrastructure\EMaigrator.Infrastructure.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Imap\EMaigrator.Connectors.Imap.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Graph\EMaigrator.Connectors.Graph.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Gmail\EMaigrator.Connectors.Gmail.csproj" />
  </ItemGroup>

</Project>
```
   Create `src/EMaigrator.Cli/CliExitCode.cs`:
```csharp
namespace EMaigrator.Cli;

/// <summary>
/// Process exit codes. 0 = success; non-zero per failure class so headless
/// callers (cron, CI, shell scripts) can branch on the reason.
/// </summary>
public enum CliExitCode
{
    Success = 0,
    UsageError = 2,
    ConnectionFailed = 3,
    PreflightBlocked = 4,
    MigrationFailed = 5,
    MigrationPartial = 6,
    ConfigError = 7,
    Cancelled = 130,
}
```
   Create `src/EMaigrator.Cli/GlobalOptions.cs`:
```csharp
using System.CommandLine;

namespace EMaigrator.Cli;

/// <summary>
/// Options shared by every command. Built once, reused everywhere.
/// All are <c>Recursive = true</c> so they are visible on every subcommand
/// (System.CommandLine 2.0 only inherits options to subcommands when recursive);
/// the integration tests invoke e.g. <c>preflight --profile p --json</c>.
/// </summary>
public static class GlobalOptions
{
    public static readonly Option<FileInfo?> Profile =
        new("--profile", "-p")
        { Description = "Path to the migration profile JSON file.", Recursive = true };

    public static readonly Option<bool> Json =
        new("--json")
        { Description = "Emit machine-readable JSON to stdout instead of human tables.", Recursive = true };

    public static readonly Option<bool> Verbose =
        new("--verbose", "-v")
        { Description = "Verbose diagnostic logging to stderr.", Recursive = true };
}
```
   Create `src/EMaigrator.Cli/Program.cs` (the `CommandFactory` lives here; subcommands are added in later tasks):
```csharp
using System.CommandLine;

namespace EMaigrator.Cli;

public static class CommandFactory
{
    public static RootCommand BuildRootCommand()
    {
        RootCommand root = new("emaigrator — non-destructive, idempotent, resumable email migration.")
        {
            Name = "emaigrator",
        };
        root.Options.Add(GlobalOptions.Profile);
        root.Options.Add(GlobalOptions.Json);
        root.Options.Add(GlobalOptions.Verbose);
        return root;
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        RootCommand root = CommandFactory.BuildRootCommand();
        return root.Parse(args).Invoke();
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~RootCommandTests` → expected **PASS** (3/3). Also create `src/EMaigrator.Cli.Tests/EMaigrator.Cli.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Cli\EMaigrator.Cli.csproj" />
  </ItemGroup>

</Project>
```
   Register both projects: `dotnet sln EMaigrator.sln add src/EMaigrator.Cli/EMaigrator.Cli.csproj src/EMaigrator.Cli.Tests/EMaigrator.Cli.Tests.csproj`.

5. - [ ] Commit:
```
git add src/EMaigrator.Cli src/EMaigrator.Cli.Tests EMaigrator.sln && git commit -m "feat(cli): scaffold CLI project with root command and exit codes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Migration profile model + loader/validator

**Goal:** Define the non-secret `MigrationProfile` document (endpoints + connection settings + scope) and a `ProfileLoader` that reads/validates the JSON, returning a `ConfigError`-mapped result and refusing any profile that embeds a plaintext secret.

**Files:**
- Create: `src/EMaigrator.Cli/Profile/MigrationProfile.cs`
- Create: `src/EMaigrator.Cli/Profile/ConnectionProfile.cs`
- Create: `src/EMaigrator.Cli/Profile/ProfileLoadResult.cs`
- Create: `src/EMaigrator.Cli/Profile/ProfileLoader.cs`
- Create: `src/EMaigrator.Cli.Tests/Profile/ProfileLoaderTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Profile/ProfileLoaderTests.cs`

**Acceptance Criteria:**
- [ ] `MigrationProfile` carries `From`/`To` `ConnectionProfile`s and a `ScopeSpec`-shaped `Scope` (bound to CONTRACTS `ScopeSpec`/`MailboxPair`), plus `StoreSubjects` and `TenantId` (defaults to `"self-host"`).
- [ ] `ConnectionProfile` carries `Provider` (`ProviderId`), `Auth` (`AuthMethod`), and a `Settings` string-dictionary — **no secret field exists on the type**.
- [ ] `ProfileLoader.Load(path)` returns `Ok` with a populated `MigrationProfile` for a valid file.
- [ ] A missing file returns `Failed` with code `ConfigError` and message naming the path.
- [ ] Malformed JSON returns `Failed` with code `ConfigError`.
- [ ] A profile whose `Settings` contains any key matching `password|secret|token|apikey|key|credential` (case-insensitive) returns `Failed` with code `ConfigError` and a message instructing the user to pass secrets via env/prompt — **plaintext secrets in the profile are rejected**.

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~ProfileLoaderTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Profile/ProfileLoaderTests.cs`:
```csharp
using EMaigrator.Cli;
using EMaigrator.Cli.Profile;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Profile;

public class ProfileLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-profile").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteProfile(string json)
    {
        string path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Loads_a_valid_profile()
    {
        string path = WriteProfile("""
        {
          "tenantId": "self-host",
          "storeSubjects": false,
          "from": { "provider": "imap", "auth": "ImapBasic",
                    "settings": { "host": "src.example.com", "port": "993", "accountEmail": "a@src.example.com" } },
          "to":   { "provider": "imap", "auth": "ImapBasic",
                    "settings": { "host": "dst.example.com", "port": "993", "accountEmail": "a@dst.example.com" } },
          "scope": { "isBatch": false, "pairs": [ { "sourceMailbox": "a@src.example.com", "destMailbox": "a@dst.example.com" } ] }
        }
        """);

        ProfileLoadResult result = ProfileLoader.Load(path);

        result.Ok.Should().BeTrue();
        result.Profile!.From.Provider.Should().Be(new ProviderId("imap"));
        result.Profile.From.Auth.Should().Be(AuthMethod.ImapBasic);
        result.Profile.To.Settings["host"].Should().Be("dst.example.com");
        result.Profile.Scope.Pairs.Should().ContainSingle()
            .Which.SourceMailbox.Should().Be("a@src.example.com");
        result.Profile.TenantId.Should().Be("self-host");
    }

    [Fact]
    public void Missing_file_is_config_error()
    {
        ProfileLoadResult result = ProfileLoader.Load(Path.Combine(_dir, "nope.json"));

        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
        result.Error.Should().Contain("nope.json");
    }

    [Fact]
    public void Malformed_json_is_config_error()
    {
        string path = WriteProfile("{ this is not json ");

        ProfileLoadResult result = ProfileLoader.Load(path);

        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Secret")]
    [InlineData("apiKey")]
    [InlineData("clientCredential")]
    public void Plaintext_secret_in_settings_is_rejected(string secretKey)
    {
        string path = WriteProfile($$"""
        {
          "from": { "provider": "imap", "auth": "ImapBasic",
                    "settings": { "host": "src.example.com", "{{secretKey}}": "hunter2" } },
          "to":   { "provider": "imap", "auth": "ImapBasic", "settings": { "host": "dst.example.com" } },
          "scope": { "isBatch": false, "pairs": [] }
        }
        """);

        ProfileLoadResult result = ProfileLoader.Load(path);

        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
        result.Error.Should().Contain("env").And.Contain("prompt");
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~ProfileLoaderTests` → expected **FAIL**: `MigrationProfile`/`ProfileLoader`/`ProfileLoadResult` do not exist.

3. - [ ] Create `src/EMaigrator.Cli/Profile/ConnectionProfile.cs`:
```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Cli.Profile;

/// <summary>
/// Non-secret connection description for one side of a migration.
/// There is deliberately NO secret field: secrets only ever come from env or prompt.
/// </summary>
public sealed record ConnectionProfile
{
    public required ProviderId Provider { get; init; }
    public required AuthMethod Auth { get; init; }
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>();
}
```
   Create `src/EMaigrator.Cli/Profile/MigrationProfile.cs`:
```csharp
using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Cli.Profile;

/// <summary>The full self-host migration description. Contains NO secrets.</summary>
public sealed record MigrationProfile
{
    public string TenantId { get; init; } = "self-host";
    public bool StoreSubjects { get; init; }
    public required ConnectionProfile From { get; init; }
    public required ConnectionProfile To { get; init; }
    public required ScopeSpec Scope { get; init; }
}
```
   Create `src/EMaigrator.Cli/Profile/ProfileLoadResult.cs`:
```csharp
namespace EMaigrator.Cli.Profile;

public sealed record ProfileLoadResult
{
    public bool Ok { get; private init; }
    public MigrationProfile? Profile { get; private init; }
    public string? Error { get; private init; }
    public CliExitCode ExitCode { get; private init; }

    public static ProfileLoadResult Success(MigrationProfile profile) =>
        new() { Ok = true, Profile = profile, ExitCode = CliExitCode.Success };

    public static ProfileLoadResult Failed(string error) =>
        new() { Ok = false, Error = error, ExitCode = CliExitCode.ConfigError };
}
```
   Create `src/EMaigrator.Cli/Profile/ProfileLoader.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Cli.Profile;

public static class ProfileLoader
{
    private static readonly string[] SecretKeyFragments =
        ["password", "secret", "token", "apikey", "key", "credential"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new ProviderIdJsonConverter(),
        },
    };

    public static ProfileLoadResult Load(string path)
    {
        if (!File.Exists(path))
            return ProfileLoadResult.Failed($"Profile file not found: {path}");

        MigrationProfile? profile;
        try
        {
            string json = File.ReadAllText(path);
            profile = JsonSerializer.Deserialize<MigrationProfile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ProfileLoadResult.Failed($"Profile file is not valid JSON: {ex.Message}");
        }

        if (profile is null)
            return ProfileLoadResult.Failed("Profile file deserialized to null.");

        foreach (ConnectionProfile side in new[] { profile.From, profile.To })
        {
            foreach (string settingKey in side.Settings.Keys)
            {
                string lower = settingKey.ToLowerInvariant();
                if (Array.Exists(SecretKeyFragments, frag => lower.Contains(frag)))
                {
                    return ProfileLoadResult.Failed(
                        $"Profile setting '{settingKey}' looks like a secret. " +
                        "Secrets must NOT be stored in the profile file. " +
                        "Pass them via an environment variable (EMAIGRATOR_SECRET_FROM / _TO) " +
                        "or the secure interactive prompt instead.");
                }
            }
        }

        return ProfileLoadResult.Success(profile);
    }
}

/// <summary>Serializes <see cref="ProviderId"/> as its bare string value ("imap"/"graph"/"gmail").</summary>
internal sealed class ProviderIdJsonConverter : JsonConverter<ProviderId>
{
    public override ProviderId Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        new(reader.GetString() ?? throw new JsonException("ProviderId must be a string."));

    public override void Write(Utf8JsonWriter writer, ProviderId value, JsonSerializerOptions o) =>
        writer.WriteStringValue(value.Value);
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~ProfileLoaderTests` → expected **PASS** (7/7 incl. theory cases).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli/Profile src/EMaigrator.Cli.Tests/Profile && git commit -m "feat(cli): migration profile model and loader that rejects plaintext secrets

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Secret input resolver (env + no-echo prompt, never CLI args)

**Goal:** Implement `SecretResolver` that obtains each side's secret strictly from a designated environment variable or a no-echo interactive prompt — never from a command-line argument — and stores it via `ISecretStore`, returning the opaque `SecretRef` to attach to the `ConnectionDescriptor`.

**Files:**
- Create: `src/EMaigrator.Cli/Secrets/IConsoleSecretReader.cs`
- Create: `src/EMaigrator.Cli/Secrets/ConsoleSecretReader.cs`
- Create: `src/EMaigrator.Cli/Secrets/SecretResolver.cs`
- Create: `src/EMaigrator.Cli.Tests/Secrets/SecretResolverTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Secrets/SecretResolverTests.cs`

**Acceptance Criteria:**
- [ ] `SecretResolver.ResolveAsync(side, profile, tenantId, ct)` reads the env var `EMAIGRATOR_SECRET_FROM` for the `from` side and `EMAIGRATOR_SECRET_TO` for the `to` side.
- [ ] If the env var is set and non-empty, that value is stored via `ISecretStore.StoreAsync(tenantId, value, ct)` and the returned `SecretRef` is used; the prompt is never shown.
- [ ] If the env var is absent, the `IConsoleSecretReader.ReadSecret(promptLabel)` no-echo reader is invoked; its value is stored the same way.
- [ ] The resolver signature accepts no secret parameter and the CLI exposes no `--password`/`--secret` option anywhere (asserted by absence in Task 4/5 option sets and the Security task).
- [ ] When auth is `GmailServiceAccountDwd`, the env var is interpreted as a path to a service-account JSON file whose **contents** are stored (not the path) — proven by a test using a temp file.
- [ ] A returned `SecretRef` is opaque (the resolver returns exactly what `ISecretStore` returned; it never returns the plaintext).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~SecretResolverTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Secrets/SecretResolverTests.cs`:
```csharp
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Secrets;

public class SecretResolverTests
{
    private static ConnectionProfile Conn(AuthMethod auth = AuthMethod.ImapBasic) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = auth,
        Settings = new Dictionary<string, string> { ["host"] = "h", ["accountEmail"] = "a@h" },
    };

    private static ISecretStore StoreReturning(string secretRef)
    {
        ISecretStore store = Substitute.For<ISecretStore>();
        store.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(secretRef);
        return store;
    }

    [Fact]
    public async Task Reads_from_env_var_when_present_and_never_prompts()
    {
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", "env-password");
        try
        {
            ISecretStore store = StoreReturning("ref-123");
            IConsoleSecretReader reader = Substitute.For<IConsoleSecretReader>();
            var resolver = new SecretResolver(store, reader);

            string secretRef = await resolver.ResolveAsync(
                MigrationSide.From, Conn(), tenantId: "t1", CancellationToken.None);

            secretRef.Should().Be("ref-123");
            await store.Received(1).StoreAsync("t1", "env-password", Arg.Any<CancellationToken>());
            reader.DidNotReceiveWithAnyArgs().ReadSecret(default!);
        }
        finally { Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null); }
    }

    [Fact]
    public async Task Falls_back_to_no_echo_prompt_when_env_missing()
    {
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", null);
        ISecretStore store = StoreReturning("ref-prompted");
        IConsoleSecretReader reader = Substitute.For<IConsoleSecretReader>();
        reader.ReadSecret(Arg.Any<string>()).Returns("typed-password");
        var resolver = new SecretResolver(store, reader);

        string secretRef = await resolver.ResolveAsync(
            MigrationSide.To, Conn(), tenantId: "t1", CancellationToken.None);

        secretRef.Should().Be("ref-prompted");
        await store.Received(1).StoreAsync("t1", "typed-password", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Service_account_env_value_is_treated_as_file_path_and_contents_are_stored()
    {
        string saPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(saPath, "{\"type\":\"service_account\",\"private_key\":\"PK\"}");
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", saPath);
        try
        {
            ISecretStore store = StoreReturning("ref-sa");
            var resolver = new SecretResolver(store, Substitute.For<IConsoleSecretReader>());

            await resolver.ResolveAsync(
                MigrationSide.From, Conn(AuthMethod.GmailServiceAccountDwd), "t1", CancellationToken.None);

            await store.Received(1).StoreAsync("t1",
                Arg.Is<string>(s => s.Contains("private_key") && s.Contains("PK")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null);
            File.Delete(saPath);
        }
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~SecretResolverTests` → expected **FAIL**: `SecretResolver`/`MigrationSide`/`IConsoleSecretReader` do not exist.

3. - [ ] Create `src/EMaigrator.Cli/Secrets/IConsoleSecretReader.cs`:
```csharp
namespace EMaigrator.Cli.Secrets;

/// <summary>Reads a secret from the terminal without echoing keystrokes.</summary>
public interface IConsoleSecretReader
{
    string ReadSecret(string promptLabel);
}
```
   Create `src/EMaigrator.Cli/Secrets/ConsoleSecretReader.cs`:
```csharp
using System.Text;

namespace EMaigrator.Cli.Secrets;

/// <summary>Default no-echo reader: masks input, supports backspace, never writes the value back.</summary>
public sealed class ConsoleSecretReader : IConsoleSecretReader
{
    public string ReadSecret(string promptLabel)
    {
        Console.Error.Write($"{promptLabel}: ");
        var sb = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true); // intercept = do not echo
            if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
        return sb.ToString();
    }
}
```
   Create `src/EMaigrator.Cli/Secrets/SecretResolver.cs`:
```csharp
using EMaigrator.Cli.Profile;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Secrets;

public enum MigrationSide { From, To }

/// <summary>
/// Obtains a side's secret from env or a no-echo prompt (NEVER a CLI arg),
/// stores it via <see cref="ISecretStore"/>, and returns the opaque SecretRef.
/// The plaintext never leaves this method.
/// </summary>
public sealed class SecretResolver(ISecretStore secretStore, IConsoleSecretReader reader)
{
    public async Task<string> ResolveAsync(
        MigrationSide side, ConnectionProfile connection, string tenantId, CancellationToken ct)
    {
        string envVar = side == MigrationSide.From ? "EMAIGRATOR_SECRET_FROM" : "EMAIGRATOR_SECRET_TO";
        string? raw = Environment.GetEnvironmentVariable(envVar);

        if (string.IsNullOrEmpty(raw))
        {
            string label = $"Secret for {side} ({connection.Provider}/{connection.Auth})";
            raw = reader.ReadSecret(label);
        }

        // Service-account auth: the env/prompt value is a *path*; store the file contents.
        string plaintext = connection.Auth == AuthMethod.GmailServiceAccountDwd && File.Exists(raw)
            ? await File.ReadAllTextAsync(raw, ct)
            : raw;

        return await secretStore.StoreAsync(tenantId, plaintext, ct);
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~SecretResolverTests` → expected **PASS** (3/3).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli/Secrets src/EMaigrator.Cli.Tests/Secrets && git commit -m "feat(cli): secret resolver reading from env or no-echo prompt, never CLI args

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Output writer (human + --json) that excludes secrets

**Goal:** Implement `IOutputWriter` with a `HumanOutputWriter` (Spectre tables) and a `JsonOutputWriter` (`System.Text.Json`) that render result DTOs, guaranteeing no secret field is ever serialized because the result DTOs carry only non-secret data.

**Files:**
- Create: `src/EMaigrator.Cli/Output/IOutputWriter.cs`
- Create: `src/EMaigrator.Cli/Output/CliResults.cs`
- Create: `src/EMaigrator.Cli/Output/JsonOutputWriter.cs`
- Create: `src/EMaigrator.Cli/Output/HumanOutputWriter.cs`
- Create: `src/EMaigrator.Cli.Tests/Output/OutputWriterTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Output/OutputWriterTests.cs`

**Acceptance Criteria:**
- [ ] `CliResults` defines record DTOs `ConnectTestOutput`, `PreflightOutput`, `RunOutput`, `StatusOutput` — none has a field for password/secret/token/`SecretRef`.
- [ ] `JsonOutputWriter.WriteConnectTest(...)` emits camelCase JSON with `ok`, `folderCount`, `messageCount`, and `errorCode` keys; serialized text contains none of `password`/`secret`/`secretRef`/`token`.
- [ ] `JsonOutputWriter.WritePreflight(...)` emits `issues[]` (with `issueType`, `severity`, `recommendedAction`) and `estimate{ mailboxCount, messageCount, totalBytes }`.
- [ ] `HumanOutputWriter` writes a non-empty human string for the same inputs (rendered via a `TextWriter`).
- [ ] A reflection-based test asserts that every public property across all `CliResults` DTOs has a name not matching `secret|password|token|credential` (case-insensitive).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~OutputWriterTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Output/OutputWriterTests.cs`:
```csharp
using System.Reflection;
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Output;

public class OutputWriterTests
{
    [Fact]
    public void Json_connect_test_emits_camelCase_and_no_secret_keys()
    {
        var sw = new StringWriter();
        var writer = new JsonOutputWriter(sw);

        writer.WriteConnectTest(new ConnectTestOutput(Ok: true, FolderCount: 12, MessageCount: 3400, ErrorCode: null));

        string json = sw.ToString();
        json.Should().Contain("\"ok\": true").And.Contain("\"folderCount\": 12").And.Contain("\"messageCount\": 3400");
        json.ToLowerInvariant().Should().NotContain("password").And.NotContain("secret").And.NotContain("token");
    }

    [Fact]
    public void Json_preflight_emits_issues_and_estimate()
    {
        var sw = new StringWriter();
        var writer = new JsonOutputWriter(sw);
        var output = new PreflightOutput(
            Issues:
            [
                new PreflightIssueOutput("FolderTooDeep", Severity.Warning, RemediationAction.FlattenFolder,
                    ["/A/B/C/D/E"], "Folder exceeds destination max depth.")
            ],
            Estimate: new EstimateOutput(MailboxCount: 1, FolderCount: 12, MessageCount: 3400, TotalBytes: 1_000_000));

        writer.WritePreflight(output);

        string json = sw.ToString();
        json.Should().Contain("\"issueType\": \"FolderTooDeep\"")
            .And.Contain("\"recommendedAction\": \"FlattenFolder\"")
            .And.Contain("\"mailboxCount\": 1")
            .And.Contain("\"messageCount\": 3400");
    }

    [Fact]
    public void Human_writer_produces_non_empty_output()
    {
        var sw = new StringWriter();
        var writer = new HumanOutputWriter(sw);

        writer.WriteConnectTest(new ConnectTestOutput(true, 12, 3400, null));

        sw.ToString().Should().Contain("12").And.Contain("3400");
    }

    [Fact]
    public void No_result_dto_property_is_named_like_a_secret()
    {
        Type[] dtoTypes =
        [
            typeof(ConnectTestOutput), typeof(PreflightOutput), typeof(PreflightIssueOutput),
            typeof(EstimateOutput), typeof(RunOutput), typeof(StatusOutput),
        ];
        string[] forbidden = ["secret", "password", "token", "credential"];

        foreach (Type t in dtoTypes)
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string lower = p.Name.ToLowerInvariant();
            forbidden.Should().NotContain(f => lower.Contains(f),
                because: $"{t.Name}.{p.Name} must not look like a secret");
        }
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~OutputWriterTests` → expected **FAIL**: output types do not exist.

3. - [ ] Create `src/EMaigrator.Cli/Output/CliResults.cs`:
```csharp
using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Cli.Output;

public sealed record ConnectTestOutput(bool Ok, int FolderCount, long MessageCount, string? ErrorCode);

public sealed record PreflightIssueOutput(
    string IssueType, Severity Severity, RemediationAction RecommendedAction,
    IReadOnlyList<string> AffectedPaths, string Description);

public sealed record EstimateOutput(int MailboxCount, int FolderCount, long MessageCount, long TotalBytes);

public sealed record PreflightOutput(IReadOnlyList<PreflightIssueOutput> Issues, EstimateOutput Estimate);

public sealed record RunOutput(
    string MailboxMigrationId, long Migrated, long Skipped, long Failed, long Pending, string Status);

public sealed record StatusOutput(
    string MailboxMigrationId, string Status, long Migrated, long Skipped, long Failed, long Pending);
```
   Create `src/EMaigrator.Cli/Output/IOutputWriter.cs`:
```csharp
namespace EMaigrator.Cli.Output;

public interface IOutputWriter
{
    void WriteConnectTest(ConnectTestOutput output);
    void WritePreflight(PreflightOutput output);
    void WriteRun(RunOutput output);
    void WriteStatus(StatusOutput output);
    void WriteError(string message);
}
```
   Create `src/EMaigrator.Cli/Output/JsonOutputWriter.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EMaigrator.Cli.Output;

public sealed class JsonOutputWriter(TextWriter output) : IOutputWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public void WriteConnectTest(ConnectTestOutput o) => output.WriteLine(JsonSerializer.Serialize(o, Options));
    public void WritePreflight(PreflightOutput o) => output.WriteLine(JsonSerializer.Serialize(o, Options));
    public void WriteRun(RunOutput o) => output.WriteLine(JsonSerializer.Serialize(o, Options));
    public void WriteStatus(StatusOutput o) => output.WriteLine(JsonSerializer.Serialize(o, Options));
    public void WriteError(string message) =>
        output.WriteLine(JsonSerializer.Serialize(new { error = message }, Options));
}
```
   Create `src/EMaigrator.Cli/Output/HumanOutputWriter.cs`:
```csharp
namespace EMaigrator.Cli.Output;

/// <summary>
/// Plain TextWriter-based human output (kept TextWriter-injectable so it is unit-testable;
/// Spectre.Console is used by the live composition root for colored tables/progress).
/// </summary>
public sealed class HumanOutputWriter(TextWriter output) : IOutputWriter
{
    public void WriteConnectTest(ConnectTestOutput o)
    {
        if (o.Ok)
            output.WriteLine($"Connection OK — {o.FolderCount} folders, {o.MessageCount} messages.");
        else
            output.WriteLine($"Connection FAILED — error: {o.ErrorCode ?? "unknown"}.");
    }

    public void WritePreflight(PreflightOutput o)
    {
        output.WriteLine($"Pre-flight: {o.Estimate.MailboxCount} mailbox(es), " +
                         $"{o.Estimate.FolderCount} folders, {o.Estimate.MessageCount} messages, " +
                         $"{o.Estimate.TotalBytes} bytes.");
        if (o.Issues.Count == 0) { output.WriteLine("No issues found."); return; }
        output.WriteLine($"{o.Issues.Count} issue(s):");
        foreach (PreflightIssueOutput i in o.Issues)
            output.WriteLine($"  [{i.Severity}] {i.IssueType}: {i.Description} " +
                             $"→ recommended: {i.RecommendedAction} (paths: {string.Join(", ", i.AffectedPaths)})");
    }

    public void WriteRun(RunOutput o) =>
        output.WriteLine($"Run {o.MailboxMigrationId}: status={o.Status} " +
                         $"migrated={o.Migrated} skipped={o.Skipped} failed={o.Failed} pending={o.Pending}.");

    public void WriteStatus(StatusOutput o) =>
        output.WriteLine($"Migration {o.MailboxMigrationId}: status={o.Status} " +
                         $"migrated={o.Migrated} skipped={o.Skipped} failed={o.Failed} pending={o.Pending}.");

    public void WriteError(string message) => output.WriteLine($"ERROR: {message}");
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~OutputWriterTests` → expected **PASS** (4/4).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli/Output src/EMaigrator.Cli.Tests/Output && git commit -m "feat(cli): human and json output writers with secret-free result DTOs

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: DI composition root + restrictive-permission profile writer (`migration new`)

**Goal:** Build `CliHostBuilder` that composes Core + Infrastructure + connectors + an in-process `IJobOrchestrator` via `Microsoft.Extensions.Hosting`, and implement `migration new` which scaffolds a starter profile JSON written with owner-only file permissions (0600 on POSIX; explicit ACL on Windows).

**Files:**
- Create: `src/EMaigrator.Cli/Hosting/CliHostBuilder.cs`
- Create: `src/EMaigrator.Cli/Io/SecureFile.cs`
- Create: `src/EMaigrator.Cli/Commands/MigrationNewCommand.cs`
- Create: `src/EMaigrator.Cli/appsettings.json`
- Modify: `src/EMaigrator.Cli/Program.cs`
- Create: `src/EMaigrator.Cli.Tests/Io/SecureFileTests.cs`
- Create: `src/EMaigrator.Cli.Tests/Commands/MigrationNewCommandTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Io/SecureFileTests.cs`, `src/EMaigrator.Cli.Tests/Commands/MigrationNewCommandTests.cs`

**Acceptance Criteria:**
- [ ] `CliHostBuilder.Build(args)` returns an `IHost` whose `Services` resolve `ISecretStore`, `ILedger`, `IPreflightAnalyzer`, `IJobOrchestrator`, `IErrorCatalog`, and every connector's `IProviderPlugin` (registered via each connector's `Add<Name>Connector()` extension per CONTRACTS §8).
- [ ] `SecureFile.WriteAllText(path, content)` creates the file and sets owner-read/write-only permissions: on POSIX the resulting mode is `600`; on Windows the file's ACL grants only the current user (no `Everyone`/`Users`/`Authenticated Users` access).
- [ ] `migration new --profile <path>` writes a valid starter profile (passes `ProfileLoader.Load` round-trip) containing **no** secret keys, and the file is created via `SecureFile` (owner-only perms).
- [ ] `migration new` on an existing path without `--force` returns `ConfigError` and does not overwrite; with `--force` it overwrites.
- [ ] `migration new` exit code is `Success` on success.

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter "FullyQualifiedName~SecureFileTests|FullyQualifiedName~MigrationNewCommandTests"` → all pass.

**Steps:**

1. - [ ] Write the failing tests. `src/EMaigrator.Cli.Tests/Io/SecureFileTests.cs`:
```csharp
using System.Runtime.InteropServices;
using EMaigrator.Cli.Io;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Io;

public class SecureFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-securefile").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Writes_content_and_restricts_to_owner_only()
    {
        string path = Path.Combine(_dir, "secret-profile.json");

        SecureFile.WriteAllText(path, "{\"hello\":\"world\"}");

        File.ReadAllText(path).Should().Be("{\"hello\":\"world\"}");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            UnixFileMode groupOther =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            (mode & groupOther).Should().Be(UnixFileMode.None, "group/other must have no access");
            (mode & UnixFileMode.UserRead).Should().Be(UnixFileMode.UserRead);
            (mode & UnixFileMode.UserWrite).Should().Be(UnixFileMode.UserWrite);
        }
        else
        {
            var fi = new FileInfo(path);
            var sec = fi.GetAccessControl();
            var rules = sec.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
            foreach (System.Security.AccessControl.FileSystemAccessRule r in rules)
            {
                string id = r.IdentityReference.Value.ToLowerInvariant();
                id.Should().NotContain("everyone").And.NotContain("users").And.NotContain("authenticated");
            }
        }
    }
}
```
   `src/EMaigrator.Cli.Tests/Commands/MigrationNewCommandTests.cs`:
```csharp
using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Profile;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class MigrationNewCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-new").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Creates_a_loadable_starter_profile_with_no_secret_keys()
    {
        string path = Path.Combine(_dir, "profile.json");

        CliExitCode code = MigrationNewCommand.Execute(path, force: false);

        code.Should().Be(CliExitCode.Success);
        ProfileLoadResult loaded = ProfileLoader.Load(path);
        loaded.Ok.Should().BeTrue("generated profile must round-trip through the loader (no plaintext secrets)");
    }

    [Fact]
    public void Refuses_to_overwrite_without_force()
    {
        string path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, "existing");

        CliExitCode code = MigrationNewCommand.Execute(path, force: false);

        code.Should().Be(CliExitCode.ConfigError);
        File.ReadAllText(path).Should().Be("existing");
    }

    [Fact]
    public void Overwrites_with_force()
    {
        string path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, "existing");

        CliExitCode code = MigrationNewCommand.Execute(path, force: true);

        code.Should().Be(CliExitCode.Success);
        ProfileLoader.Load(path).Ok.Should().BeTrue();
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter "FullyQualifiedName~SecureFileTests|FullyQualifiedName~MigrationNewCommandTests"` → expected **FAIL**: `SecureFile`/`MigrationNewCommand`/`CliHostBuilder` do not exist.

3. - [ ] Create `src/EMaigrator.Cli/Io/SecureFile.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EMaigrator.Cli.Io;

/// <summary>Writes files readable/writable only by the current user (profile & local secrets).</summary>
public static class SecureFile
{
    public static void WriteAllText(string path, string content)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WriteWindows(path, content);
        }
        else
        {
            // Create with 600 before writing so contents are never momentarily world-readable.
            using (File.Create(path)) { }
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllText(path, content);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteWindows(string path, string content)
    {
        File.WriteAllText(path, content);
        var fi = new FileInfo(path);
        FileSecurity sec = fi.GetAccessControl();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule existing in
                 sec.GetAccessRules(true, true, typeof(NTAccount)).Cast<FileSystemAccessRule>())
            sec.RemoveAccessRule(existing);

        var owner = WindowsIdentity.GetCurrent().User!;
        sec.AddAccessRule(new FileSystemAccessRule(
            owner, FileSystemRights.FullControl, AccessControlType.Allow));
        fi.SetAccessControl(sec);
    }
}
```
   Create `src/EMaigrator.Cli/Commands/MigrationNewCommand.cs`:
```csharp
using EMaigrator.Cli.Io;

namespace EMaigrator.Cli.Commands;

public static class MigrationNewCommand
{
    private const string Template = """
    {
      "tenantId": "self-host",
      "storeSubjects": false,
      "from": {
        "provider": "imap",
        "auth": "ImapBasic",
        "settings": { "host": "imap.workmail.example.com", "port": "993", "accountEmail": "user@source.example.com" }
      },
      "to": {
        "provider": "imap",
        "auth": "ImapBasic",
        "settings": { "host": "imap.dest.example.com", "port": "993", "accountEmail": "user@dest.example.com" }
      },
      "scope": {
        "isBatch": false,
        "pairs": [ { "sourceMailbox": "user@source.example.com", "destMailbox": "user@dest.example.com" } ]
      }
    }
    """;

    public static CliExitCode Execute(string path, bool force)
    {
        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine($"Refusing to overwrite existing profile '{path}'. Use --force to replace it.");
            return CliExitCode.ConfigError;
        }

        try
        {
            SecureFile.WriteAllText(path, Template);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not write profile: {ex.Message}");
            return CliExitCode.ConfigError;
        }

        Console.Error.WriteLine($"Created starter profile at '{path}' (owner-only permissions). " +
                                "Pass secrets at run time via EMAIGRATOR_SECRET_FROM/_TO or the prompt.");
        return CliExitCode.Success;
    }
}
```
   Create `src/EMaigrator.Cli/Hosting/CliHostBuilder.cs`:
```csharp
using EMaigrator.Connectors.Gmail;
using EMaigrator.Connectors.Graph;
using EMaigrator.Connectors.Imap;
using EMaigrator.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Cli.Hosting;

/// <summary>
/// The CLI composition root. Wires Core abstractions to Infrastructure implementations
/// and the connector plugins exactly as the Api/Workers do — the engine is never re-implemented.
/// For self-host single-node, the in-process orchestrator + worker are registered by
/// AddEMaigratorInfrastructure(inProcessWorker: true).
/// </summary>
public static class CliHostBuilder
{
    public static IHost Build(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "EMAIGRATOR_");

        // Infrastructure: EF/Postgres ledger, ISecretStore (LocalKey by default), Redis rate limiter,
        // MassTransit + in-process worker for self-host single-node. (Plan 03/07 supply these extensions.)
        builder.Services.AddEMaigratorInfrastructure(builder.Configuration, inProcessWorker: true);

        // Connector plugins (one IProviderPlugin each), per CONTRACTS §8 naming.
        builder.Services.AddImapConnector();
        builder.Services.AddGraphConnector();
        builder.Services.AddGmailConnector();

        // CLI services.
        builder.Services.AddSingleton<Secrets.IConsoleSecretReader, Secrets.ConsoleSecretReader>();
        builder.Services.AddSingleton<Secrets.SecretResolver>();

        return builder.Build();
    }
}
```
   Create `src/EMaigrator.Cli/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=emaigrator;Username=emaigrator;Password=",
    "Redis": "localhost:6379",
    "RabbitMq": "amqp://guest:guest@localhost:5672"
  },
  "SecretStore": { "Mode": "LocalKey", "KeyRef": "${EMAIGRATOR_LOCAL_KEY}" },
  "Retention": { "LogRetentionDays": 30 },
  "Orchestration": { "GlobalMaxConcurrentMigrations": 4, "PerTenantConcurrencyCap": 4, "BatchSize": 100 }
}
```
   Modify `src/EMaigrator.Cli/Program.cs` to register the `migration new` command (replace the body of `BuildRootCommand`'s return — add subcommands before returning):
```csharp
using System.CommandLine;
using EMaigrator.Cli.Commands;

namespace EMaigrator.Cli;

public static class CommandFactory
{
    public static RootCommand BuildRootCommand()
    {
        RootCommand root = new("emaigrator — non-destructive, idempotent, resumable email migration.")
        {
            Name = "emaigrator",
        };
        root.Options.Add(GlobalOptions.Profile);
        root.Options.Add(GlobalOptions.Json);
        root.Options.Add(GlobalOptions.Verbose);

        root.Subcommands.Add(BuildMigrationCommand());
        return root;
    }

    private static Command BuildMigrationCommand()
    {
        var migration = new Command("migration", "Manage migration profiles.");

        var newCmd = new Command("new", "Scaffold a starter migration profile file.");
        // Reuse the recursive global --profile (do NOT add a second --profile here:
        // adding a duplicate alias to a subcommand whose parent owns a recursive --profile
        // throws at build time). Only --force is local to `migration new`.
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite an existing file." };
        newCmd.Options.Add(forceOpt);
        newCmd.SetAction(parse =>
        {
            FileInfo? target = parse.GetValue(GlobalOptions.Profile);
            if (target is null)
            {
                Console.Error.WriteLine("migration new requires --profile <path> (where to write the profile).");
                return (int)CliExitCode.ConfigError;
            }
            return (int)MigrationNewCommand.Execute(target.FullName, parse.GetValue(forceOpt));
        });

        migration.Subcommands.Add(newCmd);
        return migration;
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        RootCommand root = CommandFactory.BuildRootCommand();
        return root.Parse(args).Invoke();
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter "FullyQualifiedName~SecureFileTests|FullyQualifiedName~MigrationNewCommandTests"` → expected **PASS**.

5. - [ ] Commit:
```
git add src/EMaigrator.Cli src/EMaigrator.Cli.Tests/Io src/EMaigrator.Cli.Tests/Commands && git commit -m "feat(cli): DI composition root, owner-only profile writer, migration new command

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: `connect test --side from|to` command

**Goal:** Implement `ConnectTestCommand` that loads the profile, resolves the side's secret, builds the connector via its `IProviderPlugin`, runs `TestConnectionAsync`, prints the `ConnectionTestResult`, and maps a failed test to exit code `ConnectionFailed`.

**Files:**
- Create: `src/EMaigrator.Cli/Commands/ConnectTestCommand.cs`
- Create: `src/EMaigrator.Cli/Commands/ConnectionBuilder.cs`
- Modify: `src/EMaigrator.Cli/Program.cs`
- Create: `src/EMaigrator.Cli.Tests/Commands/ConnectTestCommandTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Commands/ConnectTestCommandTests.cs`

**Acceptance Criteria:**
- [ ] `ConnectionBuilder.BuildDescriptor(ConnectionProfile, secretRef)` produces a `ConnectionDescriptor` whose `Provider`/`Auth`/`Settings` mirror the profile and whose `SecretRef` is the supplied ref (CONTRACTS §2 verbatim).
- [ ] `ConnectTestCommand.ExecuteAsync(profile, side, plugins, secretResolver, secretStore, writer, ct)` selects the matching `IProviderPlugin` by `ProviderId`, retrieves the `SecretBundle` via `ISecretStore.RetrieveAsync`, creates the source/destination, and calls `TestConnectionAsync`.
- [ ] A `ConnectionTestResult.Ok == true` → exit `Success` and the writer receives the folder/message counts.
- [ ] A `ConnectionTestResult.Ok == false` → exit `ConnectionFailed`; the printed output includes `ErrorCode` and **never** the secret.
- [ ] The `connect test` command exposes `--side` (`from`|`to`, required) and **no** secret-bearing option.
- [ ] After the test, the resolved secret is purged from the store (`ISecretStore.PurgeAsync`) so the connect-test leaves no standing secret (connect-test is not a runnable job).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~ConnectTestCommandTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Commands/ConnectTestCommandTests.cs`:
```csharp
using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class ConnectTestCommandTests
{
    private static MigrationProfile Profile(ProviderId provider) => new()
    {
        From = new ConnectionProfile { Provider = provider, Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h", ["accountEmail"] = "a@h" } },
        To = new ConnectionProfile { Provider = provider, Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h2", ["accountEmail"] = "a@h2" } },
        Scope = new Core.Diagnostics.ScopeSpec { IsBatch = false, Pairs = [] },
    };

    private static (IProviderPlugin plugin, ISourceProvider src) FakePlugin(ProviderId id, ConnectionTestResult result)
    {
        var src = Substitute.For<ISourceProvider>();
        src.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(result);
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(id);
        plugin.CreateSource(Arg.Any<ConnectionDescriptor>(), Arg.Any<SecretBundle>()).Returns(src);
        return (plugin, src);
    }

    private static (SecretResolver resolver, ISecretStore store) FakeSecrets()
    {
        var store = Substitute.For<ISecretStore>();
        store.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("ref-x");
        store.RetrieveAsync("ref-x", Arg.Any<CancellationToken>()).Returns("plaintext-pw");
        var reader = Substitute.For<IConsoleSecretReader>();
        reader.ReadSecret(Arg.Any<string>()).Returns("plaintext-pw");
        return (new SecretResolver(store, reader), store);
    }

    [Fact]
    public void BuildDescriptor_mirrors_profile_and_sets_secretRef()
    {
        ConnectionDescriptor d = ConnectionBuilder.BuildDescriptor(Profile(new ProviderId("imap")).From, "ref-x");

        d.Provider.Should().Be(new ProviderId("imap"));
        d.Auth.Should().Be(AuthMethod.ImapBasic);
        d.Settings["host"].Should().Be("h");
        d.SecretRef.Should().Be("ref-x");
    }

    [Fact]
    public async Task Ok_result_returns_success_and_writes_counts()
    {
        var id = new ProviderId("imap");
        var (plugin, _) = FakePlugin(id, new ConnectionTestResult(Ok: true, FolderCount: 7, MessageCount: 99));
        var (resolver, store) = FakeSecrets();
        var sw = new StringWriter();

        CliExitCode code = await ConnectTestCommand.ExecuteAsync(
            Profile(id), MigrationSide.From, [plugin], resolver, store, new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        sw.ToString().Should().Contain("7").And.Contain("99");
        await store.Received(1).PurgeAsync("ref-x", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_result_returns_connection_failed_and_hides_secret()
    {
        var id = new ProviderId("imap");
        var (plugin, _) = FakePlugin(id, new ConnectionTestResult(Ok: false, 0, 0, ErrorCode: "AUTH_FAILED"));
        var (resolver, store) = FakeSecrets();
        var sw = new StringWriter();

        CliExitCode code = await ConnectTestCommand.ExecuteAsync(
            Profile(id), MigrationSide.From, [plugin], resolver, store, new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.ConnectionFailed);
        sw.ToString().Should().Contain("AUTH_FAILED");
        sw.ToString().Should().NotContain("plaintext-pw");
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~ConnectTestCommandTests` → expected **FAIL**: `ConnectTestCommand`/`ConnectionBuilder` do not exist.

3. - [ ] Create `src/EMaigrator.Cli/Commands/ConnectionBuilder.cs`:
```csharp
using EMaigrator.Cli.Profile;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class ConnectionBuilder
{
    public static ConnectionDescriptor BuildDescriptor(ConnectionProfile profile, string secretRef) => new()
    {
        Provider = profile.Provider,
        Auth = profile.Auth,
        Settings = profile.Settings,
        SecretRef = secretRef,
    };
}
```
   Create `src/EMaigrator.Cli/Commands/ConnectTestCommand.cs`:
```csharp
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Cli.Commands;

public static class ConnectTestCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        MigrationProfile profile, MigrationSide side,
        IReadOnlyList<IProviderPlugin> plugins, SecretResolver secretResolver,
        ISecretStore secretStore, IOutputWriter writer, CancellationToken ct)
    {
        ConnectionProfile conn = side == MigrationSide.From ? profile.From : profile.To;
        IProviderPlugin? plugin = plugins.FirstOrDefault(p => p.Id.Equals(conn.Provider));
        if (plugin is null)
        {
            writer.WriteError($"No connector plugin registered for provider '{conn.Provider}'.");
            return CliExitCode.ConfigError;
        }

        string secretRef = await secretResolver.ResolveAsync(side, conn, profile.TenantId, ct);
        try
        {
            string plaintext = await secretStore.RetrieveAsync(secretRef, ct);
            var bundle = new SecretBundle(new Dictionary<string, string> { ["secret"] = plaintext });
            ConnectionDescriptor descriptor = ConnectionBuilder.BuildDescriptor(conn, secretRef);

            ConnectionTestResult result = side == MigrationSide.From
                ? await ExecSource(plugin, descriptor, bundle, ct)
                : await ExecDest(plugin, descriptor, bundle, ct);

            writer.WriteConnectTest(new ConnectTestOutput(
                result.Ok, result.FolderCount, result.MessageCount, result.ErrorCode));

            return result.Ok ? CliExitCode.Success : CliExitCode.ConnectionFailed;
        }
        finally
        {
            // connect-test is not a runnable job → leave no standing secret.
            await secretStore.PurgeAsync(secretRef, ct);
        }
    }

    private static async Task<ConnectionTestResult> ExecSource(
        IProviderPlugin plugin, ConnectionDescriptor d, SecretBundle b, CancellationToken ct)
    {
        await using ISourceProvider source = plugin.CreateSource(d, b);
        return await source.TestConnectionAsync(ct);
    }

    private static async Task<ConnectionTestResult> ExecDest(
        IProviderPlugin plugin, ConnectionDescriptor d, SecretBundle b, CancellationToken ct)
    {
        await using IDestinationProvider dest = plugin.CreateDestination(d, b);
        return await dest.TestConnectionAsync(ct);
    }
}
```
   Modify `src/EMaigrator.Cli/Program.cs` — add a `connect` command group with `test` (the live action resolves services from `CliHostBuilder`). Add this method and register it inside `BuildRootCommand` (`root.Subcommands.Add(BuildConnectCommand());`):
```csharp
    private static Command BuildConnectCommand()
    {
        var connect = new Command("connect", "Test provider connections.");
        var test = new Command("test", "Test a side's connection (fail fast before migrating).");
        var sideOpt = new Option<string>("--side")
        { Description = "Which side to test: from|to.", Required = true };
        sideOpt.AcceptOnlyFromAmong("from", "to");
        test.Options.Add(sideOpt);
        test.SetAction((parse, ct) =>
            CommandRunner.RunConnectTestAsync(parse, sideOpt, ct));
        connect.Subcommands.Add(test);
        return connect;
    }
```
   (The `CommandRunner` glue that loads the profile, builds the host, picks the writer, and calls `ConnectTestCommand` is implemented in Task 10; for now leave `BuildConnectCommand` wired and add a temporary `CommandRunner.RunConnectTestAsync` stub returning `(int)CliExitCode.UsageError` in `Program.cs` so the project compiles — Task 10 replaces it.)

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~ConnectTestCommandTests` → expected **PASS** (3/3).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli src/EMaigrator.Cli.Tests/Commands/ConnectTestCommandTests.cs && git commit -m "feat(cli): connect test command with secret purge and connection-failed exit code

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: `preflight` command (analyzer → plan → blocker exit code)

**Goal:** Implement `PreflightCommand` that builds both connections, runs `IPreflightAnalyzer.AnalyzeAsync`, prints the issues + estimate, and returns `PreflightBlocked` if any issue has `Severity.Blocker`, else `Success`.

**Files:**
- Create: `src/EMaigrator.Cli/Commands/PreflightCommand.cs`
- Modify: `src/EMaigrator.Cli/Program.cs`
- Create: `src/EMaigrator.Cli.Tests/Commands/PreflightCommandTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Commands/PreflightCommandTests.cs`

**Acceptance Criteria:**
- [ ] `PreflightCommand.ExecuteAsync(profile, plugins, analyzer, secretResolver, secretStore, writer, ct)` resolves both sides' secrets, builds source + destination via plugins, and calls `IPreflightAnalyzer.AnalyzeAsync(source, dest, scope, ct)` with the profile's `ScopeSpec`.
- [ ] The `PreflightPlan.Issues` are mapped to `PreflightIssueOutput` (issueType, severity, recommendedAction, affectedPaths, description) and `Estimate` to `EstimateOutput`, then written.
- [ ] Any issue with `Severity.Blocker` → exit `PreflightBlocked`; otherwise → `Success`.
- [ ] Secrets are **not** purged here (a preflight precedes a run; the run reuses the stored refs), but the printed output contains no secret values.
- [ ] Works with `--json`: the JSON output contains `issues` and `estimate` and no secret keys (asserted by reusing the JSON writer).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~PreflightCommandTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Commands/PreflightCommandTests.cs`:
```csharp
using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class PreflightCommandTests
{
    private static MigrationProfile Profile() => new()
    {
        From = new ConnectionProfile { Provider = new ProviderId("imap"), Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h" } },
        To = new ConnectionProfile { Provider = new ProviderId("imap"), Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h2" } },
        Scope = new ScopeSpec { IsBatch = false,
            Pairs = [ new MailboxPair("a@h", "a@h2") ] },
    };

    private static IProviderPlugin Plugin()
    {
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(new ProviderId("imap"));
        plugin.CreateSource(Arg.Any<ConnectionDescriptor>(), Arg.Any<SecretBundle>())
              .Returns(Substitute.For<ISourceProvider>());
        plugin.CreateDestination(Arg.Any<ConnectionDescriptor>(), Arg.Any<SecretBundle>())
              .Returns(Substitute.For<IDestinationProvider>());
        return plugin;
    }

    private static SecretResolver Resolver(out ISecretStore store)
    {
        store = Substitute.For<ISecretStore>();
        store.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("ref-x");
        store.RetrieveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("pw");
        var reader = Substitute.For<IConsoleSecretReader>();
        reader.ReadSecret(Arg.Any<string>()).Returns("pw");
        return new SecretResolver(store, reader);
    }

    private static IPreflightAnalyzer AnalyzerReturning(PreflightPlan plan)
    {
        var a = Substitute.For<IPreflightAnalyzer>();
        a.AnalyzeAsync(Arg.Any<ISourceProvider>(), Arg.Any<IDestinationProvider>(),
                       Arg.Any<ScopeSpec>(), Arg.Any<CancellationToken>()).Returns(plan);
        return a;
    }

    [Fact]
    public async Task No_blockers_returns_success_and_writes_estimate()
    {
        var plan = new PreflightPlan(
            Issues: [ new PreflightIssue("FolderTooDeep", ["/A/B/C/D/E"],
                       RemediationAction.FlattenFolder, [RemediationAction.FlattenFolder],
                       Severity.Warning, "Too deep") ],
            Estimate: new MigrationEstimate(1, 12, 3400, 1_000_000, TimeSpan.FromMinutes(5)));
        var resolver = Resolver(out ISecretStore store);
        var sw = new StringWriter();

        CliExitCode code = await PreflightCommand.ExecuteAsync(
            Profile(), [Plugin()], AnalyzerReturning(plan), resolver, store, new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        sw.ToString().Should().Contain("3400").And.Contain("FolderTooDeep");
    }

    [Fact]
    public async Task Blocker_issue_returns_preflight_blocked()
    {
        var plan = new PreflightPlan(
            Issues: [ new PreflightIssue("OverSizeCap", ["Inbox/huge"],
                       RemediationAction.SkipMessage, [RemediationAction.SkipMessage],
                       Severity.Blocker, "Exceeds 50GB cap") ],
            Estimate: new MigrationEstimate(1, 1, 1, 60_000_000_000, TimeSpan.FromHours(2)));
        var resolver = Resolver(out ISecretStore store);

        CliExitCode code = await PreflightCommand.ExecuteAsync(
            Profile(), [Plugin()], AnalyzerReturning(plan), resolver, store,
            new HumanOutputWriter(new StringWriter()), CancellationToken.None);

        code.Should().Be(CliExitCode.PreflightBlocked);
    }

    [Fact]
    public async Task Json_output_has_issues_and_no_secret_keys()
    {
        var plan = new PreflightPlan(
            Issues: [], Estimate: new MigrationEstimate(1, 2, 3, 4, TimeSpan.FromMinutes(1)));
        var resolver = Resolver(out ISecretStore store);
        var sw = new StringWriter();

        await PreflightCommand.ExecuteAsync(
            Profile(), [Plugin()], AnalyzerReturning(plan), resolver, store, new JsonOutputWriter(sw), CancellationToken.None);

        string json = sw.ToString();
        json.Should().Contain("estimate").And.Contain("issues");
        json.ToLowerInvariant().Should().NotContain("password").And.NotContain("\"pw\"");
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~PreflightCommandTests` → expected **FAIL**: `PreflightCommand` does not exist.

3. - [ ] Create `src/EMaigrator.Cli/Commands/PreflightCommand.cs`:
```csharp
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Cli.Commands;

public static class PreflightCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        MigrationProfile profile, IReadOnlyList<IProviderPlugin> plugins,
        IPreflightAnalyzer analyzer, SecretResolver secretResolver, ISecretStore secretStore,
        IOutputWriter writer, CancellationToken ct)
    {
        IProviderPlugin? fromPlugin = plugins.FirstOrDefault(p => p.Id.Equals(profile.From.Provider));
        IProviderPlugin? toPlugin = plugins.FirstOrDefault(p => p.Id.Equals(profile.To.Provider));
        if (fromPlugin is null || toPlugin is null)
        {
            writer.WriteError("Missing connector plugin for source or destination provider.");
            return CliExitCode.ConfigError;
        }

        string fromRef = await secretResolver.ResolveAsync(MigrationSide.From, profile.From, profile.TenantId, ct);
        string toRef = await secretResolver.ResolveAsync(MigrationSide.To, profile.To, profile.TenantId, ct);

        var fromBundle = new SecretBundle(
            new Dictionary<string, string> { ["secret"] = await secretStore.RetrieveAsync(fromRef, ct) });
        var toBundle = new SecretBundle(
            new Dictionary<string, string> { ["secret"] = await secretStore.RetrieveAsync(toRef, ct) });

        await using ISourceProvider source =
            fromPlugin.CreateSource(ConnectionBuilder.BuildDescriptor(profile.From, fromRef), fromBundle);
        await using IDestinationProvider dest =
            toPlugin.CreateDestination(ConnectionBuilder.BuildDescriptor(profile.To, toRef), toBundle);

        PreflightPlan plan = await analyzer.AnalyzeAsync(source, dest, profile.Scope, ct);

        var output = new PreflightOutput(
            Issues: plan.Issues.Select(i => new PreflightIssueOutput(
                i.IssueType, i.Severity, i.RecommendedAction, i.AffectedPaths, i.Description)).ToList(),
            Estimate: new EstimateOutput(
                plan.Estimate.MailboxCount, plan.Estimate.FolderCount,
                plan.Estimate.MessageCount, plan.Estimate.TotalBytes));
        writer.WritePreflight(output);

        bool blocked = plan.Issues.Any(i => i.Severity == Severity.Blocker);
        return blocked ? CliExitCode.PreflightBlocked : CliExitCode.Success;
    }
}
```
   Modify `src/EMaigrator.Cli/Program.cs` — register a top-level `preflight` command (`root.Subcommands.Add(BuildPreflightCommand());`):
```csharp
    private static Command BuildPreflightCommand()
    {
        var preflight = new Command("preflight",
            "Read-only scan: enumerate issues + estimate, gate before running.");
        preflight.SetAction((parse, ct) => CommandRunner.RunPreflightAsync(parse, ct));
        return preflight;
    }
```
   (Add a temporary `CommandRunner.RunPreflightAsync` stub returning `(int)CliExitCode.UsageError`; Task 10 implements it.)

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~PreflightCommandTests` → expected **PASS** (3/3).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli src/EMaigrator.Cli.Tests/Commands/PreflightCommandTests.cs && git commit -m "feat(cli): preflight command mapping plan to output with blocker exit code

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: `run` + `resume` commands (orchestrate, await completion, status-mapped exit code)

**Goal:** Implement `RunCommand` (and the `resume` path) that enqueue a mailbox migration via `IJobOrchestrator`, await its terminal `LedgerCounts`/status using `ILedger`, print the run summary, and map the terminal status to `Success`/`MigrationPartial`/`MigrationFailed`; `resume` re-enqueues without re-creating the migration.

**Files:**
- Create: `src/EMaigrator.Cli/Commands/RunCommand.cs`
- Create: `src/EMaigrator.Cli/Commands/IMigrationStateReader.cs`
- Modify: `src/EMaigrator.Cli/Program.cs`
- Create: `src/EMaigrator.Cli.Tests/Commands/RunCommandTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Commands/RunCommandTests.cs`

**Acceptance Criteria:**
- [ ] `RunCommand.ExecuteAsync(mailboxMigrationId, orchestrator, stateReader, ledger, writer, resume, ct)` calls `IJobOrchestrator.EnqueueMigrationAsync(mailboxMigrationId, ct)` (used identically for fresh run and resume — resume re-enqueues not-done items, per ARCHITECTURE §6).
- [ ] It polls `IMigrationStateReader.GetStatusAsync(id, ct)` until the status is terminal (`Completed|Partial|Failed|Cancelled`), then reads `ILedger.GetCountsAsync(id, ct)` and writes a `RunOutput`.
- [ ] Terminal `Completed` (Failed == 0) → `Success`; `Partial` or `Completed with Failed > 0` → `MigrationPartial`; `Failed`/`Cancelled` → `MigrationFailed`.
- [ ] When `resume == true`, `RunCommand` does **not** create a new migration row; it enqueues the existing id (the test asserts the orchestrator is called with the exact id and no creation call happens).
- [ ] A `OperationCanceledException` during polling → exit `Cancelled` (130).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~RunCommandTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Commands/RunCommandTests.cs`:
```csharp
using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class RunCommandTests
{
    private static IMigrationStateReader StateSequence(params string[] statuses)
    {
        var reader = Substitute.For<IMigrationStateReader>();
        var queue = new Queue<string>(statuses);
        reader.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(_ => queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        return reader;
    }

    private static ILedger LedgerWith(long migrated, long skipped, long failed, long pending)
    {
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(new LedgerCounts(migrated, skipped, failed, pending));
        return ledger;
    }

    [Fact]
    public async Task Completed_clean_returns_success_and_enqueues_once()
    {
        var id = Guid.NewGuid();
        var orch = Substitute.For<IJobOrchestrator>();
        var sw = new StringWriter();

        CliExitCode code = await RunCommand.ExecuteAsync(
            id, orch, StateSequence("Running", "Completed"), LedgerWith(100, 0, 0, 0),
            new HumanOutputWriter(sw), resume: false, CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        await orch.Received(1).EnqueueMigrationAsync(id, Arg.Any<CancellationToken>());
        sw.ToString().Should().Contain("100").And.Contain("Completed");
    }

    [Fact]
    public async Task Completed_with_failures_returns_partial()
    {
        var id = Guid.NewGuid();
        CliExitCode code = await RunCommand.ExecuteAsync(
            id, Substitute.For<IJobOrchestrator>(), StateSequence("Completed"),
            LedgerWith(90, 0, 10, 0), new HumanOutputWriter(new StringWriter()), false, CancellationToken.None);

        code.Should().Be(CliExitCode.MigrationPartial);
    }

    [Fact]
    public async Task Failed_status_returns_migration_failed()
    {
        var id = Guid.NewGuid();
        CliExitCode code = await RunCommand.ExecuteAsync(
            id, Substitute.For<IJobOrchestrator>(), StateSequence("Failed"),
            LedgerWith(0, 0, 0, 100), new HumanOutputWriter(new StringWriter()), false, CancellationToken.None);

        code.Should().Be(CliExitCode.MigrationFailed);
    }

    [Fact]
    public async Task Resume_enqueues_existing_id_without_creating()
    {
        var id = Guid.NewGuid();
        var orch = Substitute.For<IJobOrchestrator>();

        await RunCommand.ExecuteAsync(
            id, orch, StateSequence("Completed"), LedgerWith(10, 0, 0, 0),
            new HumanOutputWriter(new StringWriter()), resume: true, CancellationToken.None);

        await orch.Received(1).EnqueueMigrationAsync(id, Arg.Any<CancellationToken>());
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~RunCommandTests` → expected **FAIL**: `RunCommand`/`IMigrationStateReader` do not exist.

3. - [ ] Create `src/EMaigrator.Cli/Commands/IMigrationStateReader.cs`:
```csharp
namespace EMaigrator.Cli.Commands;

/// <summary>
/// Reads the current status string of a mailbox migration (mirrors MailboxMigrationStatus).
/// Implemented in the live host against the EF context; faked in unit tests.
/// </summary>
public interface IMigrationStateReader
{
    Task<string> GetStatusAsync(Guid mailboxMigrationId, CancellationToken ct);
}
```
   Create `src/EMaigrator.Cli/Commands/RunCommand.cs`:
```csharp
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class RunCommand
{
    private static readonly string[] TerminalStatuses =
        ["Completed", "Partial", "Failed", "Cancelled"];

    public static async Task<CliExitCode> ExecuteAsync(
        Guid mailboxMigrationId, IJobOrchestrator orchestrator, IMigrationStateReader stateReader,
        ILedger ledger, IOutputWriter writer, bool resume, CancellationToken ct,
        TimeSpan? pollInterval = null)
    {
        TimeSpan interval = pollInterval ?? TimeSpan.FromSeconds(2);

        try
        {
            // Resume and fresh run are identical at the orchestration seam: enqueue → workers
            // scan the ledger and (re-)process not-done items. (ARCHITECTURE.md §6)
            await orchestrator.EnqueueMigrationAsync(mailboxMigrationId, ct);

            string status;
            do
            {
                status = await stateReader.GetStatusAsync(mailboxMigrationId, ct);
                if (Array.IndexOf(TerminalStatuses, status) >= 0) break;
                await Task.Delay(interval, ct);
            } while (true);

            LedgerCounts counts = await ledger.GetCountsAsync(mailboxMigrationId, ct);
            writer.WriteRun(new RunOutput(
                mailboxMigrationId.ToString(), counts.Migrated, counts.Skipped, counts.Failed, counts.Pending, status));

            return MapExit(status, counts);
        }
        catch (OperationCanceledException)
        {
            writer.WriteError("Run cancelled.");
            return CliExitCode.Cancelled;
        }
    }

    private static CliExitCode MapExit(string status, LedgerCounts counts) => status switch
    {
        "Completed" when counts.Failed == 0 => CliExitCode.Success,
        "Completed" => CliExitCode.MigrationPartial,
        "Partial" => CliExitCode.MigrationPartial,
        _ => CliExitCode.MigrationFailed, // Failed | Cancelled
    };
}
```
   Modify `src/EMaigrator.Cli/Program.cs` — register `run` and `resume` (`root.Subcommands.Add(BuildRunCommand()); root.Subcommands.Add(BuildResumeCommand());`):
```csharp
    private static Command BuildRunCommand()
    {
        var run = new Command("run", "Run the migration to completion (self-host in-process worker).");
        var idOpt = new Option<Guid?>("--id")
        { Description = "Existing mailbox-migration id; omit to create from the profile." };
        run.Options.Add(idOpt);
        run.SetAction((parse, ct) => CommandRunner.RunMigrationAsync(parse, idOpt, resume: false, ct));
        return run;
    }

    private static Command BuildResumeCommand()
    {
        var resume = new Command("resume", "Re-enqueue not-done items for an existing migration.");
        var idOpt = new Option<Guid>("--id")
        { Description = "Existing mailbox-migration id to resume.", Required = true };
        resume.Options.Add(idOpt);
        resume.SetAction((parse, ct) => CommandRunner.RunMigrationAsync(parse, idOpt!, resume: true, ct));
        return resume;
    }
```
   (Add a temporary `CommandRunner.RunMigrationAsync` overload stub; Task 10 implements it.)

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~RunCommandTests` → expected **PASS** (4/4).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli src/EMaigrator.Cli.Tests/Commands/RunCommandTests.cs && git commit -m "feat(cli): run and resume commands with status-mapped exit codes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: `status` + `report` commands

**Goal:** Implement `StatusCommand` (prints current status + `LedgerCounts` for a migration id) and `ReportCommand` (writes a CSV report of ledger entries to a file or stdout), both honoring `--json`.

**Files:**
- Create: `src/EMaigrator.Cli/Commands/StatusCommand.cs`
- Create: `src/EMaigrator.Cli/Commands/ReportCommand.cs`
- Modify: `src/EMaigrator.Cli/Program.cs`
- Create: `src/EMaigrator.Cli.Tests/Commands/StatusReportCommandTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Commands/StatusReportCommandTests.cs`

**Acceptance Criteria:**
- [ ] `StatusCommand.ExecuteAsync(id, stateReader, ledger, writer, ct)` writes a `StatusOutput` and returns `Success`.
- [ ] `ReportCommand.ExecuteAsync(id, ledger, csvWriter, ct)` writes a CSV with header `identityKey,sourceFolder,destFolder,status,errorCode,updatedAt` and one row per `LedgerEntry` from `ILedger.GetNotDoneAsync`/counts; the CSV contains **no** body/subject/sender/recipient columns.
- [ ] CSV cell values are quote-escaped (a value containing a comma or quote is wrapped in quotes with `"` doubled).
- [ ] `report` returns `Success` and writes nothing secret.

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~StatusReportCommandTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Commands/StatusReportCommandTests.cs`:
```csharp
using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class StatusReportCommandTests
{
    private static ILedger Ledger(LedgerCounts counts, params LedgerEntry[] entries)
    {
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(counts);
        ledger.GetNotDoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(_ => ToAsync(entries));
        return ledger;
    }

    private static async IAsyncEnumerable<LedgerEntry> ToAsync(LedgerEntry[] entries)
    {
        foreach (var e in entries) { yield return e; }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Status_writes_counts_and_returns_success()
    {
        var id = Guid.NewGuid();
        var reader = Substitute.For<IMigrationStateReader>();
        reader.GetStatusAsync(id, Arg.Any<CancellationToken>()).Returns("Running");
        var sw = new StringWriter();

        CliExitCode code = await StatusCommand.ExecuteAsync(
            id, reader, Ledger(new LedgerCounts(50, 2, 1, 47)), new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        sw.ToString().Should().Contain("Running").And.Contain("50");
    }

    [Fact]
    public async Task Report_csv_has_metadata_only_header_and_no_content_columns()
    {
        var id = Guid.NewGuid();
        var entry = new LedgerEntry(id, "mid:<abc@x>", "Inbox", "Inbox",
            LedgerStatus.Failed, "WRITE_FAILED", DateTimeOffset.UnixEpoch);
        var sw = new StringWriter();

        CliExitCode code = await ReportCommand.ExecuteAsync(
            id, Ledger(new LedgerCounts(0, 0, 1, 0), entry), sw, CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        string csv = sw.ToString();
        csv.Should().StartWith("identityKey,sourceFolder,destFolder,status,errorCode,updatedAt");
        csv.Should().Contain("mid:<abc@x>").And.Contain("WRITE_FAILED");
        csv.ToLowerInvariant().Should().NotContain("body").And.NotContain("subject")
            .And.NotContain("sender").And.NotContain("recipient");
    }

    [Fact]
    public async Task Report_csv_escapes_commas_and_quotes()
    {
        var id = Guid.NewGuid();
        var entry = new LedgerEntry(id, "mid:<a,b>", "A \"B\"", "C", LedgerStatus.Migrated, null, DateTimeOffset.UnixEpoch);
        var sw = new StringWriter();

        await ReportCommand.ExecuteAsync(id, Ledger(new LedgerCounts(1, 0, 0, 0), entry), sw, CancellationToken.None);

        sw.ToString().Should().Contain("\"mid:<a,b>\"").And.Contain("\"A \"\"B\"\"\"");
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~StatusReportCommandTests` → expected **FAIL**: `StatusCommand`/`ReportCommand` do not exist.

3. - [ ] Create `src/EMaigrator.Cli/Commands/StatusCommand.cs`:
```csharp
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class StatusCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        Guid mailboxMigrationId, IMigrationStateReader stateReader, ILedger ledger,
        IOutputWriter writer, CancellationToken ct)
    {
        string status = await stateReader.GetStatusAsync(mailboxMigrationId, ct);
        LedgerCounts c = await ledger.GetCountsAsync(mailboxMigrationId, ct);
        writer.WriteStatus(new StatusOutput(
            mailboxMigrationId.ToString(), status, c.Migrated, c.Skipped, c.Failed, c.Pending));
        return CliExitCode.Success;
    }
}
```
   Create `src/EMaigrator.Cli/Commands/ReportCommand.cs`:
```csharp
using System.Text;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class ReportCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        Guid mailboxMigrationId, ILedger ledger, TextWriter csvOut, CancellationToken ct)
    {
        await csvOut.WriteLineAsync("identityKey,sourceFolder,destFolder,status,errorCode,updatedAt");
        await foreach (LedgerEntry e in ledger.GetNotDoneAsync(mailboxMigrationId, ct))
        {
            var row = new StringBuilder()
                .Append(Csv(e.IdentityKey)).Append(',')
                .Append(Csv(e.SourceFolder)).Append(',')
                .Append(Csv(e.DestFolder)).Append(',')
                .Append(Csv(e.Status.ToString())).Append(',')
                .Append(Csv(e.ErrorCode ?? "")).Append(',')
                .Append(Csv(e.UpdatedAt.ToString("O")));
            await csvOut.WriteLineAsync(row.ToString());
        }
        return CliExitCode.Success;
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
```
   Modify `src/EMaigrator.Cli/Program.cs` — register `status` and `report` (`root.Subcommands.Add(BuildStatusCommand()); root.Subcommands.Add(BuildReportCommand());`):
```csharp
    private static Command BuildStatusCommand()
    {
        var status = new Command("status", "Show a migration's current status and counts.");
        var idOpt = new Option<Guid>("--id") { Description = "Mailbox-migration id.", Required = true };
        status.Options.Add(idOpt);
        status.SetAction((parse, ct) => CommandRunner.RunStatusAsync(parse, idOpt, ct));
        return status;
    }

    private static Command BuildReportCommand()
    {
        var report = new Command("report", "Export a metadata-only CSV report of ledger entries.");
        var idOpt = new Option<Guid>("--id") { Description = "Mailbox-migration id.", Required = true };
        var outOpt = new Option<FileInfo?>("--out", "-o") { Description = "CSV file (default: stdout)." };
        report.Options.Add(idOpt);
        report.Options.Add(outOpt);
        report.SetAction((parse, ct) => CommandRunner.RunReportAsync(parse, idOpt, outOpt, ct));
        return report;
    }
```
   (Add temporary `CommandRunner.RunStatusAsync` / `RunReportAsync` stubs; Task 10 implements them.)

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~StatusReportCommandTests` → expected **PASS** (3/3).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli src/EMaigrator.Cli.Tests/Commands/StatusReportCommandTests.cs && git commit -m "feat(cli): status and metadata-only csv report commands

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: `CommandRunner` glue (host wiring, writer selection, migration creation)

**Goal:** Implement `CommandRunner` that replaces the Task 6–9 stubs — it builds the `IHost`, loads/validates the profile, picks the writer from `--json`, resolves the registered services, creates a fresh `MailboxMigration` from the profile for `run` (via the persistence seam), and dispatches to each command, mapping `ProfileLoadResult` failures to exit codes.

**Files:**
- Create: `src/EMaigrator.Cli/Commands/CommandRunner.cs`
- Create: `src/EMaigrator.Cli/Commands/IMigrationFactory.cs`
- Modify: `src/EMaigrator.Cli/Program.cs` (remove temporary stubs)
- Create: `src/EMaigrator.Cli.Tests/Commands/CommandRunnerTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Commands/CommandRunnerTests.cs`

**Acceptance Criteria:**
- [ ] `CommandRunner.SelectWriter(json, textWriter)` returns a `JsonOutputWriter` when `json` is true and a `HumanOutputWriter` otherwise.
- [ ] `CommandRunner.ResolveProfile(profileOption)` returns `ProfileLoadResult.Failed`/`ConfigError` when `--profile` is null/missing and the loaded profile otherwise.
- [ ] `IMigrationFactory.CreateAsync(profile, ct)` is the seam that persists a new `MailboxMigration` (one per `MailboxPair`) and returns its `Guid`; the live impl lives in Infrastructure access, faked in the unit test.
- [ ] A unit test proves `SelectWriter` and `ResolveProfile` behave per spec without needing a live host.
- [ ] All previously-stubbed `CommandRunner.Run*Async` methods are now real (compile + the full suite builds).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~CommandRunnerTests` → all pass; and `dotnet build src/EMaigrator.Cli` → Build succeeded.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Commands/CommandRunnerTests.cs`:
```csharp
using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class CommandRunnerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-runner").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SelectWriter_returns_json_writer_when_json_true()
    {
        IOutputWriter writer = CommandRunner.SelectWriter(json: true, new StringWriter());
        writer.Should().BeOfType<JsonOutputWriter>();
    }

    [Fact]
    public void SelectWriter_returns_human_writer_when_json_false()
    {
        IOutputWriter writer = CommandRunner.SelectWriter(json: false, new StringWriter());
        writer.Should().BeOfType<HumanOutputWriter>();
    }

    [Fact]
    public void ResolveProfile_returns_config_error_when_option_null()
    {
        var result = CommandRunner.ResolveProfile(profilePath: null);
        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
    }

    [Fact]
    public void ResolveProfile_loads_existing_valid_profile()
    {
        string path = Path.Combine(_dir, "p.json");
        File.WriteAllText(path, """
        { "from": { "provider": "imap", "auth": "ImapBasic", "settings": { "host": "h" } },
          "to":   { "provider": "imap", "auth": "ImapBasic", "settings": { "host": "h2" } },
          "scope": { "isBatch": false, "pairs": [] } }
        """);

        var result = CommandRunner.ResolveProfile(path);

        result.Ok.Should().BeTrue();
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~CommandRunnerTests` → expected **FAIL**: `CommandRunner.SelectWriter`/`ResolveProfile` not implemented (currently a stub).

3. - [ ] Create `src/EMaigrator.Cli/Commands/IMigrationFactory.cs`:
```csharp
using EMaigrator.Cli.Profile;

namespace EMaigrator.Cli.Commands;

/// <summary>
/// Persists a new MailboxMigration (one per MailboxPair in scope) and returns the first id to run.
/// Live impl provided by Infrastructure access in the host; faked in tests.
/// </summary>
public interface IMigrationFactory
{
    Task<Guid> CreateAsync(MigrationProfile profile, CancellationToken ct);
}
```
   Create `src/EMaigrator.Cli/Commands/CommandRunner.cs`:
```csharp
using System.CommandLine;
using EMaigrator.Cli.Hosting;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Cli.Commands;

public static class CommandRunner
{
    public static IOutputWriter SelectWriter(bool json, TextWriter output) =>
        json ? new JsonOutputWriter(output) : new HumanOutputWriter(output);

    public static ProfileLoadResult ResolveProfile(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            return ProfileLoadResult.Failed("No --profile specified. Pass --profile <path>.");
        return ProfileLoader.Load(profilePath);
    }

    private static (IHost host, MigrationProfile profile, IOutputWriter writer, CliExitCode? earlyExit)
        Bootstrap(ParseResult parse)
    {
        bool json = parse.GetValue(GlobalOptions.Json);
        FileInfo? profileFile = parse.GetValue(GlobalOptions.Profile);
        IOutputWriter writer = SelectWriter(json, Console.Out);

        ProfileLoadResult loaded = ResolveProfile(profileFile?.FullName);
        if (!loaded.Ok)
        {
            writer.WriteError(loaded.Error!);
            return (null!, null!, writer, loaded.ExitCode);
        }

        IHost host = CliHostBuilder.Build([]);
        return (host, loaded.Profile!, writer, null);
    }

    public static async Task<int> RunConnectTestAsync(ParseResult parse, Option<string> sideOpt, CancellationToken ct)
    {
        var (host, profile, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            var plugins = host.Services.GetServices<IProviderPlugin>().ToList();
            var resolver = host.Services.GetRequiredService<SecretResolver>();
            var store = host.Services.GetRequiredService<ISecretStore>();
            MigrationSide side = parse.GetValue(sideOpt) == "to" ? MigrationSide.To : MigrationSide.From;
            return (int)await ConnectTestCommand.ExecuteAsync(profile, side, plugins, resolver, store, writer, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunPreflightAsync(ParseResult parse, CancellationToken ct)
    {
        var (host, profile, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            var plugins = host.Services.GetServices<IProviderPlugin>().ToList();
            return (int)await PreflightCommand.ExecuteAsync(
                profile, plugins,
                host.Services.GetRequiredService<IPreflightAnalyzer>(),
                host.Services.GetRequiredService<SecretResolver>(),
                host.Services.GetRequiredService<ISecretStore>(), writer, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunMigrationAsync(ParseResult parse, Option<Guid?> idOpt, bool resume, CancellationToken ct)
    {
        var (host, profile, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            Guid? supplied = parse.GetValue(idOpt);
            Guid id = supplied ?? await host.Services.GetRequiredService<IMigrationFactory>().CreateAsync(profile, ct);
            return (int)await RunCommand.ExecuteAsync(
                id, host.Services.GetRequiredService<IJobOrchestrator>(),
                host.Services.GetRequiredService<IMigrationStateReader>(),
                host.Services.GetRequiredService<ILedger>(), writer, resume, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunMigrationAsync(ParseResult parse, Option<Guid> idOpt, bool resume, CancellationToken ct)
    {
        var (host, _, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            Guid id = parse.GetValue(idOpt);
            return (int)await RunCommand.ExecuteAsync(
                id, host.Services.GetRequiredService<IJobOrchestrator>(),
                host.Services.GetRequiredService<IMigrationStateReader>(),
                host.Services.GetRequiredService<ILedger>(), writer, resume, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunStatusAsync(ParseResult parse, Option<Guid> idOpt, CancellationToken ct)
    {
        var (host, _, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            return (int)await StatusCommand.ExecuteAsync(
                parse.GetValue(idOpt),
                host.Services.GetRequiredService<IMigrationStateReader>(),
                host.Services.GetRequiredService<ILedger>(), writer, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunReportAsync(ParseResult parse, Option<Guid> idOpt, Option<FileInfo?> outOpt, CancellationToken ct)
    {
        var (host, _, _, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            FileInfo? outFile = parse.GetValue(outOpt);
            TextWriter csv = outFile is null ? Console.Out : new StreamWriter(outFile.FullName, append: false);
            try
            {
                return (int)await ReportCommand.ExecuteAsync(
                    parse.GetValue(idOpt), host.Services.GetRequiredService<ILedger>(), csv, ct);
            }
            finally { if (outFile is not null) await csv.DisposeAsync(); }
        }
        finally { await host.StopAsync(ct); }
    }
}
```
   Modify `src/EMaigrator.Cli/Program.cs` — remove every temporary `CommandRunner` stub method added in Tasks 6–9 (they now live in `CommandRunner.cs`); keep only the `Build*Command` factory methods and `Main`.

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~CommandRunnerTests` → expected **PASS** (4/4). Then `dotnet build src/EMaigrator.Cli` → **Build succeeded**.

5. - [ ] Commit:
```
git add src/EMaigrator.Cli src/EMaigrator.Cli.Tests/Commands/CommandRunnerTests.cs && git commit -m "feat(cli): command runner glue wiring host, writer selection, migration creation

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: Command-parsing unit tests (all commands wired, exit-code mapping)

**Goal:** Add parse-level unit tests proving every command/option is registered correctly, `--side` only accepts `from|to`, unknown commands map to `UsageError`, and the global options are visible on subcommands.

**Files:**
- Create: `src/EMaigrator.Cli.Tests/Parsing/CommandParsingTests.cs`
- Test: `src/EMaigrator.Cli.Tests/Parsing/CommandParsingTests.cs`

**Acceptance Criteria:**
- [ ] Parsing `migration new --profile x.json` yields zero parse errors and resolves the `new` subcommand.
- [ ] Parsing `connect test --side sideways` yields a parse error (only `from|to` allowed).
- [ ] Parsing `connect test --side from` yields zero parse errors.
- [ ] Parsing `preflight`, `run`, `resume --id <guid>`, `status --id <guid>`, `report --id <guid>` each resolve to the correct command with zero parse errors.
- [ ] Parsing an unknown command (`bogus`) produces a parse error; invoking it returns a non-zero exit code.
- [ ] `resume` and `status` require `--id` (omitting it yields a parse error).

**Verify:** `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~CommandParsingTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.Tests/Parsing/CommandParsingTests.cs`:
```csharp
using System.CommandLine;
using EMaigrator.Cli;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Parsing;

public class CommandParsingTests
{
    private static ParseResult Parse(string args) =>
        CommandFactory.BuildRootCommand().Parse(args);

    [Fact]
    public void Migration_new_parses_clean()
    {
        ParseResult r = Parse("migration new --profile x.json");
        r.Errors.Should().BeEmpty();
        r.CommandResult.Command.Name.Should().Be("new");
    }

    [Fact]
    public void Connect_test_rejects_invalid_side()
    {
        ParseResult r = Parse("connect test --side sideways");
        r.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Connect_test_accepts_from()
    {
        ParseResult r = Parse("connect test --side from");
        r.Errors.Should().BeEmpty();
        r.CommandResult.Command.Name.Should().Be("test");
    }

    [Theory]
    [InlineData("preflight", "preflight")]
    [InlineData("run", "run")]
    public void Top_level_commands_parse_clean(string args, string expectedName)
    {
        ParseResult r = Parse(args);
        r.Errors.Should().BeEmpty();
        r.CommandResult.Command.Name.Should().Be(expectedName);
    }

    [Fact]
    public void Resume_requires_id()
    {
        Parse("resume").Errors.Should().NotBeEmpty();
        Parse($"resume --id {Guid.NewGuid()}").Errors.Should().BeEmpty();
    }

    [Fact]
    public void Status_and_report_require_id()
    {
        Parse("status").Errors.Should().NotBeEmpty();
        Parse("report").Errors.Should().NotBeEmpty();
        Parse($"status --id {Guid.NewGuid()}").Errors.Should().BeEmpty();
        Parse($"report --id {Guid.NewGuid()}").Errors.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_command_is_a_parse_error_and_nonzero_exit()
    {
        ParseResult r = Parse("bogus");
        r.Errors.Should().NotBeEmpty();
        r.Invoke().Should().NotBe((int)CliExitCode.Success);
    }

    [Fact]
    public void No_command_exposes_a_password_or_secret_option_anywhere()
    {
        // Defense in depth against accidentally adding a secret-bearing flag.
        RootCommand root = CommandFactory.BuildRootCommand();
        AssertNoSecretOption(root);

        static void AssertNoSecretOption(Command cmd)
        {
            foreach (Option o in cmd.Options)
            {
                string n = o.Name.ToLowerInvariant();
                n.Should().NotContain("password").And.NotContain("secret").And.NotContain("token");
            }
            foreach (Command sub in cmd.Subcommands) AssertNoSecretOption(sub);
        }
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~CommandParsingTests` → expected **FAIL** initially only if any command isn't wired; if all of Tasks 5–10 are in place this confirms wiring (it is a regression guard). If any case fails, fix the offending `Build*Command` registration in `Program.cs`.

3. - [ ] Make any wiring fixes needed in `src/EMaigrator.Cli/Program.cs` so all assertions pass (e.g., ensure `--side` uses `AcceptOnlyFromAmong("from","to")`, `resume`/`status`/`report` `--id` options are `Required = true`). No new production types are introduced by this task — it is a wiring/regression guard.

4. - [ ] Run `dotnet test src/EMaigrator.Cli.Tests --filter FullyQualifiedName~CommandParsingTests` → expected **PASS** (all cases).

5. - [ ] Commit:
```
git add src/EMaigrator.Cli.Tests/Parsing src/EMaigrator.Cli/Program.cs && git commit -m "test(cli): command-parsing guards for all commands and no-secret-option invariant

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: Integration test — preflight + run against GreenMail IMAP (Testcontainers), exit code 0

**Goal:** Prove the CLI's headline behavior end-to-end: against real containerized Postgres + RabbitMQ + Redis + GreenMail IMAP (both sides), `emaigrator preflight` then `emaigrator run` complete a real migration of seeded messages and the `run` process exits `0`.

**Files:**
- Create: `src/EMaigrator.Cli.IntegrationTests/EMaigrator.Cli.IntegrationTests.csproj`
- Create: `src/EMaigrator.Cli.IntegrationTests/GreenMailCliFixture.cs`
- Create: `src/EMaigrator.Cli.IntegrationTests/PreflightRunE2ETests.cs`
- Test: `src/EMaigrator.Cli.IntegrationTests/PreflightRunE2ETests.cs`

**Acceptance Criteria:**
- [ ] The fixture starts containers: Postgres, RabbitMQ, Redis, and GreenMail (exposing IMAP on 3143/3993) configured with two users (source + dest) via GreenMail env (`GREENMAIL_OPTS`).
- [ ] The test seeds N=20 messages into the source mailbox over IMAP (MailKit), writes a profile JSON (IMAP→IMAP, both pointing at GreenMail with the right ports), and sets `EMAIGRATOR_SECRET_FROM`/`_TO` to the IMAP passwords.
- [ ] Running the CLI `preflight` returns exit `0` (no blockers for a clean tree) and prints an estimate `messageCount == 20`.
- [ ] Running the CLI `run` returns exit `0` and the destination mailbox over IMAP contains exactly 20 messages.
- [ ] The test asserts no plaintext password appears anywhere in the captured stdout/stderr of either invocation.

**Verify:** `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~PreflightRunE2ETests` → all pass (Docker required).

**Steps:**

1. - [ ] Write the failing test. First `src/EMaigrator.Cli.IntegrationTests/EMaigrator.Cli.IntegrationTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="Testcontainers" Version="4.0.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.0.0" />
    <PackageReference Include="Testcontainers.RabbitMq" Version="4.0.0" />
    <PackageReference Include="Testcontainers.Redis" Version="4.0.0" />
    <PackageReference Include="MailKit" Version="4.8.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Cli\EMaigrator.Cli.csproj" />
  </ItemGroup>

</Project>
```
   `src/EMaigrator.Cli.IntegrationTests/GreenMailCliFixture.cs`:
```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace EMaigrator.Cli.IntegrationTests;

public sealed class GreenMailCliFixture : IAsyncLifetime
{
    public const string SourceUser = "source@greenmail.local";
    public const string DestUser = "dest@greenmail.local";
    public const string SourcePassword = "src-pass-123";
    public const string DestPassword = "dst-pass-456";

    public PostgreSqlContainer Postgres { get; } =
        new PostgreSqlBuilder().WithDatabase("emaigrator").WithUsername("emaigrator").WithPassword("pg-pass").Build();
    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder().Build();
    public RedisContainer Redis { get; } = new RedisBuilder().Build();

    public IContainer GreenMail { get; private set; } = default!;
    public int ImapPort { get; private set; }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync(), Redis.StartAsync());

        GreenMail = new ContainerBuilder()
            .WithImage("greenmail/standalone:2.1.0")
            .WithEnvironment("GREENMAIL_OPTS",
                "-Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled=false " +
                $"-Dgreenmail.users={SourceUser}:{SourcePassword},{DestUser}:{DestPassword}")
            .WithPortBinding(3143, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3143))
            .Build();
        await GreenMail.StartAsync();
        ImapPort = GreenMail.GetMappedPublicPort(3143);
    }

    public async Task DisposeAsync()
    {
        await GreenMail.DisposeAsync();
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(),
            RabbitMq.DisposeAsync().AsTask(), Redis.DisposeAsync().AsTask());
    }
}
```
   `src/EMaigrator.Cli.IntegrationTests/PreflightRunE2ETests.cs`:
```csharp
using System.Text;
using EMaigrator.Cli;
using FluentAssertions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Xunit;

namespace EMaigrator.Cli.IntegrationTests;

public class PreflightRunE2ETests(GreenMailCliFixture fx) : IClassFixture<GreenMailCliFixture>
{
    private const int SeedCount = 20;

    private async Task SeedSourceAsync()
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(GreenMailCliFixture.SourceUser, GreenMailCliFixture.SourcePassword);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite);
        for (int i = 0; i < SeedCount; i++)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("Sender", "sender@x.com"));
            msg.To.Add(new MailboxAddress("Dest", GreenMailCliFixture.DestUser));
            msg.Subject = $"Seed {i}";
            msg.MessageId = $"<seed-{i}@greenmail.local>";
            msg.Body = new TextPart("plain") { Text = $"Body {i}" };
            await inbox.AppendAsync(msg);
        }
        await client.DisconnectAsync(true);
    }

    private async Task<int> CountDestAsync()
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(GreenMailCliFixture.DestUser, GreenMailCliFixture.DestPassword);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
        int count = client.Inbox.Count;
        await client.DisconnectAsync(true);
        return count;
    }

    private string WriteProfile(string dir)
    {
        string path = Path.Combine(dir, "profile.json");
        File.WriteAllText(path, $$"""
        {
          "tenantId": "self-host",
          "storeSubjects": false,
          "from": { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.SourceUser}}" } },
          "to":   { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.DestUser}}" } },
          "scope": { "isBatch": false,
            "pairs": [ { "sourceMailbox": "{{GreenMailCliFixture.SourceUser}}",
                         "destMailbox": "{{GreenMailCliFixture.DestUser}}" } ] }
        }
        """);
        return path;
    }

    private async Task<(int exit, string output)> InvokeCliAsync(string[] args)
    {
        var sw = new StringWriter();
        TextWriter prevOut = Console.Out, prevErr = Console.Error;
        Console.SetOut(sw); Console.SetError(sw);
        try
        {
            int exit = await CommandFactory.BuildRootCommand().Parse(args).InvokeAsync();
            return (exit, sw.ToString());
        }
        finally { Console.SetOut(prevOut); Console.SetError(prevErr); }
    }

    [Fact]
    public async Task Preflight_then_run_migrates_all_messages_and_exits_zero()
    {
        await SeedSourceAsync();
        string dir = Directory.CreateTempSubdirectory("emaigrator-e2e").FullName;
        string profile = WriteProfile(dir);

        // Point the host at the test containers + provide secrets via env (never CLI args).
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Postgres", fx.Postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Redis", fx.Redis.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__RabbitMq", fx.RabbitMq.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", GreenMailCliFixture.SourcePassword);
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", GreenMailCliFixture.DestPassword);
        try
        {
            (int preExit, string preOut) = await InvokeCliAsync(["preflight", "--profile", profile, "--json"]);
            preExit.Should().Be((int)CliExitCode.Success);
            preOut.Should().Contain("\"messageCount\": 20");

            (int runExit, string runOut) = await InvokeCliAsync(["run", "--profile", profile, "--json"]);
            runExit.Should().Be((int)CliExitCode.Success);

            (await CountDestAsync()).Should().Be(SeedCount);

            // Security: no plaintext password ever appears in CLI output.
            (preOut + runOut).Should().NotContain(GreenMailCliFixture.SourcePassword)
                                       .And.NotContain(GreenMailCliFixture.DestPassword);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null);
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", null);
            Directory.Delete(dir, recursive: true);
        }
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~PreflightRunE2ETests` → expected **FAIL** first run: project not registered / host env mapping (`EMAIGRATOR_ConnectionStrings__*`) not honored until the `CliHostBuilder` env prefix + the `IMigrationFactory`/`IMigrationStateReader` live impls (from Infrastructure, Plan 03/07) are present.

3. - [ ] Make it green: register the project (`dotnet sln EMaigrator.sln add src/EMaigrator.Cli.IntegrationTests/EMaigrator.Cli.IntegrationTests.csproj`); confirm `CliHostBuilder.Build` calls `AddEnvironmentVariables(prefix: "EMAIGRATOR_")` so `EMAIGRATOR_ConnectionStrings__Postgres` overrides `appsettings.json` (already wired in Task 5). Ensure the live `IMigrationFactory` and `IMigrationStateReader` are registered by `AddEMaigratorInfrastructure(..., inProcessWorker: true)` (these live impls are supplied by Plan 03/07; this task binds to them — if absent at execution time, add thin adapters in `EMaigrator.Cli/Hosting/` that use the EF `DbContext` + the in-process orchestrator). Add the in-process worker registration so `run` actually processes the queue within the CLI process.

4. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~PreflightRunE2ETests` → expected **PASS**: preflight exit 0 with `messageCount == 20`, run exit 0, destination has 20 messages, no password in output.

5. - [ ] Commit:
```
git add src/EMaigrator.Cli.IntegrationTests EMaigrator.sln && git commit -m "test(cli): e2e preflight+run migration against GreenMail via Testcontainers

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: Integration test — resume re-migrates only not-done items

**Goal:** Prove `emaigrator resume` re-enqueues an interrupted migration and completes it without duplicating already-migrated messages — running the same migration twice yields exactly the seeded count at the destination (idempotency), exit `0`.

**Files:**
- Create: `src/EMaigrator.Cli.IntegrationTests/ResumeE2ETests.cs`
- Test: `src/EMaigrator.Cli.IntegrationTests/ResumeE2ETests.cs`

**Acceptance Criteria:**
- [ ] The test seeds 20 messages, runs a migration to completion once, then invokes `emaigrator resume --id <id>` (reusing the same `MailboxMigration` id from the first run).
- [ ] After resume, the destination mailbox still contains exactly 20 messages (no duplicates — the ledger marks them done; resume re-enqueues but workers skip done items).
- [ ] The resume invocation exits `0` (`Success`).
- [ ] A second seed of 5 *additional* messages into source, then a `resume`, results in 25 destination messages — proving resume picks up newly-not-done items while skipping the original 20.

**Verify:** `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~ResumeE2ETests` → all pass (Docker required).

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.IntegrationTests/ResumeE2ETests.cs`:
```csharp
using EMaigrator.Cli;
using FluentAssertions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Xunit;

namespace EMaigrator.Cli.IntegrationTests;

public class ResumeE2ETests(GreenMailCliFixture fx) : IClassFixture<GreenMailCliFixture>
{
    private async Task SeedAsync(int from, int count)
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(GreenMailCliFixture.SourceUser, GreenMailCliFixture.SourcePassword);
        await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
        for (int i = from; i < from + count; i++)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("S", "s@x.com"));
            msg.To.Add(new MailboxAddress("D", GreenMailCliFixture.DestUser));
            msg.Subject = $"Resume {i}";
            msg.MessageId = $"<resume-{i}@greenmail.local>";
            msg.Body = new TextPart("plain") { Text = $"Body {i}" };
            await client.Inbox.AppendAsync(msg);
        }
        await client.DisconnectAsync(true);
    }

    private async Task<int> CountDestAsync()
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(GreenMailCliFixture.DestUser, GreenMailCliFixture.DestPassword);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
        int c = client.Inbox.Count;
        await client.DisconnectAsync(true);
        return c;
    }

    private string WriteProfile(string dir)
    {
        string path = Path.Combine(dir, "profile.json");
        File.WriteAllText(path, $$"""
        { "tenantId": "self-host", "storeSubjects": false,
          "from": { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.SourceUser}}" } },
          "to":   { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.DestUser}}" } },
          "scope": { "isBatch": false,
            "pairs": [ { "sourceMailbox": "{{GreenMailCliFixture.SourceUser}}",
                         "destMailbox": "{{GreenMailCliFixture.DestUser}}" } ] } }
        """);
        return path;
    }

    private async Task<(int exit, string output)> InvokeAsync(string[] args)
    {
        var sw = new StringWriter();
        TextWriter o = Console.Out, e = Console.Error;
        Console.SetOut(sw); Console.SetError(sw);
        try { return (await CommandFactory.BuildRootCommand().Parse(args).InvokeAsync(), sw.ToString()); }
        finally { Console.SetOut(o); Console.SetError(e); }
    }

    [Fact]
    public async Task Resume_is_idempotent_and_picks_up_new_items()
    {
        await SeedAsync(0, 20);
        string dir = Directory.CreateTempSubdirectory("emaigrator-resume").FullName;
        string profile = WriteProfile(dir);

        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Postgres", fx.Postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Redis", fx.Redis.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__RabbitMq", fx.RabbitMq.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", GreenMailCliFixture.SourcePassword);
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", GreenMailCliFixture.DestPassword);
        try
        {
            // First run: emit JSON so we can read back the mailboxMigrationId.
            (int runExit, string runOut) = await InvokeAsync(["run", "--profile", profile, "--json"]);
            runExit.Should().Be((int)CliExitCode.Success);
            (await CountDestAsync()).Should().Be(20);

            string id = ExtractMigrationId(runOut);

            // Resume the same id — already-done items are skipped, no duplicates.
            (int r1, _) = await InvokeAsync(["resume", "--id", id, "--profile", profile, "--json"]);
            r1.Should().Be((int)CliExitCode.Success);
            (await CountDestAsync()).Should().Be(20);

            // Add 5 more to source, resume again — only the new 5 migrate.
            await SeedAsync(20, 5);
            (int r2, _) = await InvokeAsync(["resume", "--id", id, "--profile", profile, "--json"]);
            r2.Should().Be((int)CliExitCode.Success);
            (await CountDestAsync()).Should().Be(25);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null);
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string ExtractMigrationId(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            json[json.IndexOf('{')..(json.LastIndexOf('}') + 1)]);
        return doc.RootElement.GetProperty("mailboxMigrationId").GetString()!;
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~ResumeE2ETests` → expected **FAIL** first run until `RunOutput.MailboxMigrationId` is present in JSON (it is, from Task 4/8) and the live `resume` path enqueues the existing id (Task 8/10). If the second-seed step migrates duplicates, fix is in the worker/ledger (Plan 07) — but the CLI assertion stands.

3. - [ ] Make it green: confirm the JSON `run` output includes `mailboxMigrationId` (Task 4 `RunOutput`), confirm `resume --id` routes through `CommandRunner.RunMigrationAsync(parse, idOpt, resume: true, ct)` (Task 10) which calls `IJobOrchestrator.EnqueueMigrationAsync(id, ...)` for the existing id, and that the in-process worker re-scans the ledger (Plan 07 behavior) so done items are skipped. No new CLI production code is expected; if a gap exists it is a wiring fix in `CommandRunner`/`CliHostBuilder`.

4. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~ResumeE2ETests` → expected **PASS**: 20 → resume → 20 → +5 → resume → 25, all exit 0.

5. - [ ] Commit:
```
git add src/EMaigrator.Cli.IntegrationTests/ResumeE2ETests.cs && git commit -m "test(cli): e2e resume is idempotent and picks up newly-not-done items

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 14: Security Verification — credentials never plaintext args/echoed, restrictive perms, --json excludes secrets

**Goal:** Prove the CLI's security focus from the INDEX per-plan table: credentials are never accepted as plaintext CLI args nor echoed to stdout/logs, the profile/config file is written with restrictive permissions, and `--json` output excludes secrets.

**USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Cli.IntegrationTests/Security/CredentialHandlingSecurityTests.cs`
- Test: `src/EMaigrator.Cli.IntegrationTests/Security/CredentialHandlingSecurityTests.cs`

**Acceptance Criteria:**
- [ ] **No secret CLI option exists:** a reflection walk of the full command tree (`CommandFactory.BuildRootCommand()`) finds zero options whose name matches `password|secret|token|credential|apikey` (captured assertion output).
- [ ] **Env secret absent from captured output:** running `connect test --side from --profile <p> --json` against GreenMail with the password set ONLY in `EMAIGRATOR_SECRET_FROM` produces stdout+stderr that contain **zero** occurrences of the literal password string (grep-style `.Contains` returns false; the count is asserted == 0).
- [ ] **Profile file perms restrictive:** `migration new --profile <p>` produces a file that on POSIX has mode `600` (no group/other bits) and on Windows has an ACL with no `Everyone`/`Users`/`Authenticated Users` entry — re-checked here independently of Task 5's unit test, against the real CLI invocation.
- [ ] **--json excludes secrets:** the JSON emitted by `preflight --json` and `connect test --json` is parsed with `JsonDocument`; a recursive walk of every property name and string value asserts none equals/contains the password and no key matches `secret|password|token|secretRef`.
- [ ] **SecretRef not leaked:** the `--json` connect-test/preflight output contains no `secretRef` key and no value equal to the opaque ref returned by the store.
- [ ] All assertions run via `dotnet test` and the captured failing/passing output is recorded in the task's evidence.

**Verify:** `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~CredentialHandlingSecurityTests` → all pass (Docker required); capture full console output as evidence.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.IntegrationTests/Security/CredentialHandlingSecurityTests.cs`:
```csharp
using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text.Json;
using EMaigrator.Cli;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.IntegrationTests.Security;

public class CredentialHandlingSecurityTests(GreenMailCliFixture fx) : IClassFixture<GreenMailCliFixture>
{
    private string WriteProfile(string dir)
    {
        string path = Path.Combine(dir, "profile.json");
        File.WriteAllText(path, $$"""
        { "tenantId": "self-host", "storeSubjects": false,
          "from": { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.SourceUser}}" } },
          "to":   { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.DestUser}}" } },
          "scope": { "isBatch": false,
            "pairs": [ { "sourceMailbox": "{{GreenMailCliFixture.SourceUser}}",
                         "destMailbox": "{{GreenMailCliFixture.DestUser}}" } ] } }
        """);
        return path;
    }

    private async Task<(int exit, string output)> InvokeAsync(string[] args)
    {
        var sw = new StringWriter();
        TextWriter o = Console.Out, e = Console.Error;
        Console.SetOut(sw); Console.SetError(sw);
        try { return (await CommandFactory.BuildRootCommand().Parse(args).InvokeAsync(), sw.ToString()); }
        finally { Console.SetOut(o); Console.SetError(e); }
    }

    [Fact]
    public void No_secret_bearing_option_exists_anywhere_in_command_tree()
    {
        string[] forbidden = ["password", "secret", "token", "credential", "apikey"];
        var offenders = new List<string>();

        void Walk(Command cmd, string prefix)
        {
            foreach (Option opt in cmd.Options)
            {
                string n = opt.Name.ToLowerInvariant();
                if (forbidden.Any(f => n.Contains(f))) offenders.Add($"{prefix} {opt.Name}");
            }
            foreach (Command sub in cmd.Subcommands) Walk(sub, $"{prefix} {sub.Name}");
        }
        Walk(CommandFactory.BuildRootCommand(), "emaigrator");

        offenders.Should().BeEmpty("the CLI must never accept a secret as a command-line argument");
    }

    [Fact]
    public async Task Env_secret_never_appears_in_captured_output()
    {
        string dir = Directory.CreateTempSubdirectory("emaigrator-sec").FullName;
        string profile = WriteProfile(dir);
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Postgres", fx.Postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Redis", fx.Redis.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__RabbitMq", fx.RabbitMq.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", GreenMailCliFixture.SourcePassword);
        try
        {
            (int exit, string output) = await InvokeAsync(["connect", "test", "--side", "from", "--profile", profile, "--json"]);

            exit.Should().Be((int)CliExitCode.Success);
            CountOccurrences(output, GreenMailCliFixture.SourcePassword).Should().Be(0);
            output.ToLowerInvariant().Should().NotContain("secretref");
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migration_new_writes_owner_only_profile_file()
    {
        string dir = Directory.CreateTempSubdirectory("emaigrator-sec-perm").FullName;
        try
        {
            string path = Path.Combine(dir, "p.json");
            int exit = CommandFactory.BuildRootCommand().Parse(["migration", "new", "--profile", path]).Invoke();
            exit.Should().Be((int)CliExitCode.Success);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                UnixFileMode mode = File.GetUnixFileMode(path);
                UnixFileMode groupOther =
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                (mode & groupOther).Should().Be(UnixFileMode.None);
            }
            else
            {
                var sec = new FileInfo(path).GetAccessControl();
                foreach (System.Security.AccessControl.FileSystemAccessRule r in
                         sec.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount)))
                {
                    string id = r.IdentityReference.Value.ToLowerInvariant();
                    id.Should().NotContain("everyone").And.NotContain("users").And.NotContain("authenticated");
                }
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Json_output_contains_no_secret_keys_or_values()
    {
        string dir = Directory.CreateTempSubdirectory("emaigrator-sec-json").FullName;
        string profile = WriteProfile(dir);
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Postgres", fx.Postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Redis", fx.Redis.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__RabbitMq", fx.RabbitMq.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", GreenMailCliFixture.SourcePassword);
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", GreenMailCliFixture.DestPassword);
        try
        {
            (_, string preOut) = await InvokeAsync(["preflight", "--profile", profile, "--json"]);

            string json = preOut[preOut.IndexOf('{')..(preOut.LastIndexOf('}') + 1)];
            using var doc = JsonDocument.Parse(json);
            AssertNoSecret(doc.RootElement,
                [GreenMailCliFixture.SourcePassword, GreenMailCliFixture.DestPassword]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null);
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AssertNoSecret(JsonElement el, string[] secrets)
    {
        string[] forbiddenKeys = ["secret", "password", "token", "secretref", "credential"];
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty p in el.EnumerateObject())
                {
                    forbiddenKeys.Should().NotContain(k => p.Name.ToLowerInvariant().Contains(k));
                    AssertNoSecret(p.Value, secrets);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in el.EnumerateArray()) AssertNoSecret(item, secrets);
                break;
            case JsonValueKind.String:
                string s = el.GetString() ?? "";
                secrets.Should().NotContain(secret => s.Contains(secret));
                break;
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~CredentialHandlingSecurityTests` → expected **FAIL** if any leak exists (e.g., a secret-bearing option slipped in, output echoes the password, profile perms too loose, or a `secretRef` reaches `--json`). Capture the failing output.

3. - [ ] Fix any leak independently and minimally at its source: remove/rename any offending option in `Program.cs`; ensure `HumanOutputWriter`/`JsonOutputWriter` only ever serialize `CliResults` DTOs (never a `SecretBundle`/descriptor); confirm `SecureFile` is the only writer for the profile; confirm `ConnectionTestResult.RawDetail` is **not** surfaced to output (only `Ok`/counts/`ErrorCode` are). Re-run after each fix.

4. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~CredentialHandlingSecurityTests` → expected **PASS** (4/4). Capture the passing output as the gate's evidence.

5. - [ ] Commit:
```
git add src/EMaigrator.Cli.IntegrationTests/Security && git commit -m "test(cli): security gate — no plaintext-arg secrets, restrictive perms, secret-free json

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 15: Functional Verification — full CLI happy-path acceptance (new → connect test → preflight → run → status → report)

**Goal:** Prove the CLI subsystem's headline behavior end-to-end as one acceptance flow: scaffold a profile, test both connections, preflight, run, check status, and export a report — all green with correct exit codes against the real GreenMail/Testcontainers stack.

**Files:**
- Create: `src/EMaigrator.Cli.IntegrationTests/FullCliFlowAcceptanceTests.cs`
- Test: `src/EMaigrator.Cli.IntegrationTests/FullCliFlowAcceptanceTests.cs`

**Acceptance Criteria:**
- [ ] `migration new --profile <p>` exits `0` and produces a loadable profile (then the test fills GreenMail host/port/accounts by rewriting it, since `new` uses example hosts).
- [ ] `connect test --side from` and `connect test --side to` both exit `0` against GreenMail.
- [ ] `preflight --json` exits `0` and reports `messageCount == 20`.
- [ ] `run --json` exits `0`; destination has 20 messages.
- [ ] `status --id <id> --json` exits `0` and reports a terminal status with `migrated == 20` and `failed == 0`.
- [ ] `report --id <id> --out <csv>` exits `0`; the CSV's header is the metadata-only header and the file contains 0 not-done rows after a clean run (or only failed rows, of which there are none) — and contains no body/subject/sender/recipient columns.

**Verify:** `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~FullCliFlowAcceptanceTests` → all pass (Docker required).

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Cli.IntegrationTests/FullCliFlowAcceptanceTests.cs`:
```csharp
using System.Text.Json;
using EMaigrator.Cli;
using FluentAssertions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Xunit;

namespace EMaigrator.Cli.IntegrationTests;

public class FullCliFlowAcceptanceTests(GreenMailCliFixture fx) : IClassFixture<GreenMailCliFixture>
{
    private async Task SeedAsync(int count)
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(GreenMailCliFixture.SourceUser, GreenMailCliFixture.SourcePassword);
        await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
        for (int i = 0; i < count; i++)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("S", "s@x.com"));
            msg.To.Add(new MailboxAddress("D", GreenMailCliFixture.DestUser));
            msg.Subject = $"Flow {i}";
            msg.MessageId = $"<flow-{i}@greenmail.local>";
            msg.Body = new TextPart("plain") { Text = $"Body {i}" };
            await client.Inbox.AppendAsync(msg);
        }
        await client.DisconnectAsync(true);
    }

    private void RewriteProfileForGreenMail(string path)
    {
        File.WriteAllText(path, $$"""
        { "tenantId": "self-host", "storeSubjects": false,
          "from": { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.SourceUser}}" } },
          "to":   { "provider": "imap", "auth": "ImapBasic",
            "settings": { "host": "127.0.0.1", "port": "{{fx.ImapPort}}", "useTls": "false",
                          "accountEmail": "{{GreenMailCliFixture.DestUser}}" } },
          "scope": { "isBatch": false,
            "pairs": [ { "sourceMailbox": "{{GreenMailCliFixture.SourceUser}}",
                         "destMailbox": "{{GreenMailCliFixture.DestUser}}" } ] } }
        """);
    }

    private async Task<(int exit, string output)> InvokeAsync(string[] args)
    {
        var sw = new StringWriter();
        TextWriter o = Console.Out, e = Console.Error;
        Console.SetOut(sw); Console.SetError(sw);
        try { return (await CommandFactory.BuildRootCommand().Parse(args).InvokeAsync(), sw.ToString()); }
        finally { Console.SetOut(o); Console.SetError(e); }
    }

    [Fact]
    public async Task Full_happy_path_flow_is_green()
    {
        await SeedAsync(20);
        string dir = Directory.CreateTempSubdirectory("emaigrator-flow").FullName;
        string profile = Path.Combine(dir, "p.json");
        string csv = Path.Combine(dir, "report.csv");

        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Postgres", fx.Postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__Redis", fx.Redis.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_ConnectionStrings__RabbitMq", fx.RabbitMq.GetConnectionString());
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", GreenMailCliFixture.SourcePassword);
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", GreenMailCliFixture.DestPassword);
        try
        {
            (int newExit, _) = await InvokeAsync(["migration", "new", "--profile", profile]);
            newExit.Should().Be((int)CliExitCode.Success);
            RewriteProfileForGreenMail(profile); // template uses example hosts; point at GreenMail

            (await InvokeAsync(["connect", "test", "--side", "from", "--profile", profile])).exit
                .Should().Be((int)CliExitCode.Success);
            (await InvokeAsync(["connect", "test", "--side", "to", "--profile", profile])).exit
                .Should().Be((int)CliExitCode.Success);

            (int preExit, string preOut) = await InvokeAsync(["preflight", "--profile", profile, "--json"]);
            preExit.Should().Be((int)CliExitCode.Success);
            preOut.Should().Contain("\"messageCount\": 20");

            (int runExit, string runOut) = await InvokeAsync(["run", "--profile", profile, "--json"]);
            runExit.Should().Be((int)CliExitCode.Success);
            string id = ExtractId(runOut);

            (int statusExit, string statusOut) = await InvokeAsync(["status", "--id", id, "--profile", profile, "--json"]);
            statusExit.Should().Be((int)CliExitCode.Success);
            using (var doc = JsonDocument.Parse(statusOut[statusOut.IndexOf('{')..(statusOut.LastIndexOf('}') + 1)]))
            {
                doc.RootElement.GetProperty("migrated").GetInt64().Should().Be(20);
                doc.RootElement.GetProperty("failed").GetInt64().Should().Be(0);
            }

            (int reportExit, _) = await InvokeAsync(["report", "--id", id, "--out", csv, "--profile", profile]);
            reportExit.Should().Be((int)CliExitCode.Success);
            string csvText = File.ReadAllText(csv);
            csvText.Should().StartWith("identityKey,sourceFolder,destFolder,status,errorCode,updatedAt");
            csvText.ToLowerInvariant().Should().NotContain("body").And.NotContain("subject")
                .And.NotContain("sender").And.NotContain("recipient");
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null);
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string ExtractId(string json)
    {
        using var doc = JsonDocument.Parse(json[json.IndexOf('{')..(json.LastIndexOf('}') + 1)]);
        return doc.RootElement.GetProperty("mailboxMigrationId").GetString()!;
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~FullCliFlowAcceptanceTests` → expected **FAIL** until every command is wired through `CommandRunner` against the live host (Tasks 5–10) and the Infrastructure/Worker live impls are registered.

3. - [ ] Make it green: ensure all six commands dispatch correctly and the in-process worker drains the queue during `run`; confirm `status` reads the terminal `migrated`/`failed` counts via `ILedger.GetCountsAsync` and `report` exports the metadata-only CSV. Any remaining failure is a wiring/registration fix in `CommandRunner`/`CliHostBuilder`, not new feature code.

4. - [ ] Run `dotnet test src/EMaigrator.Cli.IntegrationTests --filter FullyQualifiedName~FullCliFlowAcceptanceTests` → expected **PASS**: every step exits 0, 20 migrated, 0 failed, metadata-only report.

5. - [ ] Commit:
```
git add src/EMaigrator.Cli.IntegrationTests/FullCliFlowAcceptanceTests.cs && git commit -m "test(cli): functional acceptance — full new→test→preflight→run→status→report flow green

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
