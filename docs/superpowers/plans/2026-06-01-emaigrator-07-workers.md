# EMaigrator.Workers (orchestration & streaming) Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Implement the orchestration consumers and streaming migration engine for `EMaigrator.Workers`: a four-stage MassTransit pipeline (`StartMigration` → `MigrateFolder` → `MigrateBatch` → message copy) that fans a mailbox-pair out into per-folder tasks, applies the operator-approved structural remediations, pages source messages into bounded batches, streams each message from source to destination in-flight (bodies never persisted), checkpoints every copy in the idempotency ledger, paces all provider calls through the Redis token-bucket rate limiter, parks poison messages in the DLQ as content-free needs-decision events, and supports pause/resume/cancel plus crash-resume — all driven by `OrchestrationOptions`.

**Architecture:** Stateless MassTransit consumers (background services in `EMaigrator.Workers`) consume the frozen `EMaigrator.Core.Contracts` messages and compose `EMaigrator.Core.Abstractions` seams (`ISourceProvider`, `IDestinationProvider`, `ILedger`, `IRateLimiter`, `IProviderPlugin`, `ISecretStore`) supplied by `EMaigrator.Infrastructure` and the connector assemblies via DI. All migration state lives in Postgres (the ledger) and the queue; workers hold nothing durable, so any worker can pick up any work and a dead worker's un-acked batches simply redeliver. The Message is the idempotent atom — each copy re-checks the ledger before writing, making at-least-once delivery safe with zero duplicates.

**Tech Stack:** C#/.NET 10 (LTS), MassTransit 8 (RabbitMQ transport) with `MassTransit.TestFramework` in-memory harness for unit tests, xUnit + FluentAssertions + NSubstitute for unit tests, Testcontainers (`Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `Testcontainers.Redis`) + a GreenMail IMAP container for the E2E pipeline, Serilog/OpenTelemetry for instrumentation. References only `EMaigrator.Core` (abstractions/contracts/model/config) and `EMaigrator.Infrastructure` (concrete ledger/rate-limiter/secret-store/MassTransit wiring) per the dependency rule (DESIGN.md §15).

---

### Task 1: PauseRegistry — distributed pause/cancel gate

**Goal:** Implement a `IMigrationControlGate` that records pause/cancel requests per job in Redis so every stateless worker consistently stops pulling new batches for a paused/cancelled job while letting in-flight batches drain.

**Files:**
- Create: `src/EMaigrator.Workers/Control/IMigrationControlGate.cs`
- Create: `src/EMaigrator.Workers/Control/RedisMigrationControlGate.cs`
- Create: `src/EMaigrator.Workers/Control/MigrationControlState.cs`
- Test: `src/EMaigrator.Workers.Tests/Control/RedisMigrationControlGateTests.cs`

**Acceptance Criteria:**
- [ ] `IMigrationControlGate` exposes `Task<MigrationControlState> GetStateAsync(Guid jobId, CancellationToken ct)`, `Task PauseAsync(Guid jobId, CancellationToken ct)`, `Task ResumeAsync(Guid jobId, CancellationToken ct)`, `Task CancelAsync(Guid jobId, CancellationToken ct)`.
- [ ] `MigrationControlState` is `enum { Active, Paused, Cancelled }`; default for an unknown job is `Active`.
- [ ] Pause sets state `Paused`; Resume clears back to `Active`; Cancel sets `Cancelled` and is terminal (a later Resume on a cancelled job leaves it `Cancelled`).
- [ ] State is stored in Redis under key `emaigrator:control:{jobId}` so all worker instances observe the same value.
- [ ] All methods pass `ct` through to the Redis client.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~RedisMigrationControlGateTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Control/RedisMigrationControlGateTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Workers.Control;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace EMaigrator.Workers.Tests.Control;

public sealed class RedisMigrationControlGateTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    private ConnectionMultiplexer _mux = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _mux.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private RedisMigrationControlGate Gate() => new(_mux);

    [Fact]
    public async Task Unknown_job_is_active()
    {
        var gate = Gate();
        var state = await gate.GetStateAsync(Guid.NewGuid(), CancellationToken.None);
        state.Should().Be(MigrationControlState.Active);
    }

    [Fact]
    public async Task Pause_then_resume_roundtrips()
    {
        var gate = Gate();
        var job = Guid.NewGuid();
        await gate.PauseAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Paused);
        await gate.ResumeAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Active);
    }

    [Fact]
    public async Task Cancel_is_terminal_and_survives_resume()
    {
        var gate = Gate();
        var job = Guid.NewGuid();
        await gate.CancelAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Cancelled);
        await gate.ResumeAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Cancelled);
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~RedisMigrationControlGateTests` → expected **FAIL**: `IMigrationControlGate`, `RedisMigrationControlGate`, and `MigrationControlState` do not exist (compile error CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Control/MigrationControlState.cs`:

```csharp
namespace EMaigrator.Workers.Control;

public enum MigrationControlState
{
    Active = 0,
    Paused = 1,
    Cancelled = 2
}
```

   Implement `src/EMaigrator.Workers/Control/IMigrationControlGate.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Control;

/// <summary>
/// Distributed pause/cancel gate. Every stateless worker consults this before pulling new work
/// for a job, so Pause/Cancel take effect uniformly across the worker pool while in-flight
/// batches drain. State lives in Redis (the shared backplane).
/// </summary>
public interface IMigrationControlGate
{
    Task<MigrationControlState> GetStateAsync(Guid jobId, CancellationToken ct);
    Task PauseAsync(Guid jobId, CancellationToken ct);
    Task ResumeAsync(Guid jobId, CancellationToken ct);
    Task CancelAsync(Guid jobId, CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Control/RedisMigrationControlGate.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace EMaigrator.Workers.Control;

public sealed class RedisMigrationControlGate : IMigrationControlGate
{
    private readonly IConnectionMultiplexer _mux;

    public RedisMigrationControlGate(IConnectionMultiplexer mux) => _mux = mux;

    private static RedisKey Key(Guid jobId) => $"emaigrator:control:{jobId:N}";

    public async Task<MigrationControlState> GetStateAsync(Guid jobId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = _mux.GetDatabase();
        var value = await db.StringGetAsync(Key(jobId)).WaitAsync(ct);
        if (value.IsNullOrEmpty)
            return MigrationControlState.Active;
        return (MigrationControlState)(int)value;
    }

    public async Task PauseAsync(Guid jobId, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        // Do not override a terminal Cancel.
        if (await GetStateAsync(jobId, ct) == MigrationControlState.Cancelled) return;
        await db.StringSetAsync(Key(jobId), (int)MigrationControlState.Paused).WaitAsync(ct);
    }

    public async Task ResumeAsync(Guid jobId, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        if (await GetStateAsync(jobId, ct) == MigrationControlState.Cancelled) return;
        await db.StringSetAsync(Key(jobId), (int)MigrationControlState.Active).WaitAsync(ct);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        await db.StringSetAsync(Key(jobId), (int)MigrationControlState.Cancelled).WaitAsync(ct);
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~RedisMigrationControlGateTests` → expected **PASS** (3 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Control src/EMaigrator.Workers.Tests/Control
git commit -m "feat(workers): redis-backed migration control gate for pause/resume/cancel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: RemediationPlanStore — resolve approved structural remediations per migration

**Goal:** Implement an `IRemediationPlanStore` that returns the operator-approved `RemediationAction` for a given source folder of a mailbox migration, so the `StartMigration` consumer can apply Flatten/Sanitize/Rename when computing destination folders.

**Files:**
- Create: `src/EMaigrator.Workers/Remediation/IRemediationPlanStore.cs`
- Create: `src/EMaigrator.Workers/Remediation/ApprovedRemediation.cs`
- Create: `src/EMaigrator.Workers/Remediation/FolderRemediationResolver.cs`
- Test: `src/EMaigrator.Workers.Tests/Remediation/FolderRemediationResolverTests.cs`

**Acceptance Criteria:**
- [ ] `ApprovedRemediation` is a record `(string SourceFolder, RemediationAction Action)` using `EMaigrator.Core.Diagnostics.RemediationAction`.
- [ ] `IRemediationPlanStore` exposes `Task<IReadOnlyList<ApprovedRemediation>> GetApprovedAsync(Guid mailboxMigrationId, CancellationToken ct)`.
- [ ] `FolderRemediationResolver.Resolve(FolderPath source, IReadOnlyList<ApprovedRemediation> approved, ProviderConstraints destConstraints)` returns the destination `FolderPath`:
  - `FlattenFolder` → `FolderFlattener.Flatten(source, destConstraints.MaxFolderDepth)`.
  - `SanitizeFolderName` → `FolderSanitizer.Sanitize(source, destConstraints)`.
  - No approved action for that folder → source path unchanged (still `/`-canonical).
- [ ] Resolver is pure (no I/O) and binds to `FolderFlattener`/`FolderSanitizer`/`ProviderConstraints`/`FolderPath`/`RemediationAction` from CONTRACTS verbatim.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~FolderRemediationResolverTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Remediation/FolderRemediationResolverTests.cs`:

```csharp
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Remediation;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Workers.Tests.Remediation;

public sealed class FolderRemediationResolverTests
{
    private static readonly ProviderConstraints Outlook = new()
    {
        MaxFolderDepth = 3,
        IllegalNameChars = new[] { '\\', ':' }
    };

    [Fact]
    public void No_remediation_returns_source_unchanged()
    {
        var src = FolderPath.Parse("Inbox/Clients");
        var dest = FolderRemediationResolver.Resolve(src, new List<ApprovedRemediation>(), Outlook);
        dest.ToString().Should().Be("Inbox/Clients");
    }

    [Fact]
    public void Flatten_action_collapses_to_max_depth()
    {
        var src = FolderPath.Parse("A/B/C/D/E");
        var approved = new List<ApprovedRemediation>
        {
            new("A/B/C/D/E", RemediationAction.FlattenFolder)
        };
        var dest = FolderRemediationResolver.Resolve(src, approved, Outlook);
        dest.Depth.Should().BeLessThanOrEqualTo(Outlook.MaxFolderDepth);
        dest.ToString().Should().Be(FolderFlattener.Flatten(src, Outlook.MaxFolderDepth).ToString());
    }

    [Fact]
    public void Sanitize_action_strips_illegal_chars()
    {
        var src = FolderPath.Parse(@"Inbox/Cli:ents");
        var approved = new List<ApprovedRemediation>
        {
            new(@"Inbox/Cli:ents", RemediationAction.SanitizeFolderName)
        };
        var dest = FolderRemediationResolver.Resolve(src, approved, Outlook);
        dest.ToString().Should().NotContain(":");
        dest.ToString().Should().Be(FolderSanitizer.Sanitize(src, Outlook).ToString());
    }

    [Fact]
    public void Remediation_matches_only_the_named_folder()
    {
        var src = FolderPath.Parse("Inbox/Other");
        var approved = new List<ApprovedRemediation>
        {
            new("A/B/C/D/E", RemediationAction.FlattenFolder)
        };
        var dest = FolderRemediationResolver.Resolve(src, approved, Outlook);
        dest.ToString().Should().Be("Inbox/Other");
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~FolderRemediationResolverTests` → expected **FAIL**: `ApprovedRemediation` and `FolderRemediationResolver` do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Remediation/ApprovedRemediation.cs`:

```csharp
using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Workers.Remediation;

/// <summary>An operator-approved structural remediation for a single source folder.</summary>
public sealed record ApprovedRemediation(string SourceFolder, RemediationAction Action);
```

   Implement `src/EMaigrator.Workers/Remediation/IRemediationPlanStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Remediation;

/// <summary>
/// Supplies the structural remediations the operator approved at pre-flight (DESIGN.md §7).
/// Implemented in Infrastructure over the persisted approval; faked in unit tests.
/// </summary>
public interface IRemediationPlanStore
{
    Task<IReadOnlyList<ApprovedRemediation>> GetApprovedAsync(Guid mailboxMigrationId, CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Remediation/FolderRemediationResolver.cs`:

```csharp
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Remediation;

/// <summary>
/// Pure: maps a source folder to its destination folder by applying the approved structural
/// remediation. No silent defaults — only explicitly-approved actions transform the path
/// (DESIGN.md §7 "no silent defaults").
/// </summary>
public static class FolderRemediationResolver
{
    public static FolderPath Resolve(
        FolderPath source,
        IReadOnlyList<ApprovedRemediation> approved,
        ProviderConstraints destConstraints)
    {
        var sourceKey = source.ToString();
        RemediationAction action = RemediationAction.None;
        foreach (var r in approved)
        {
            if (r.SourceFolder == sourceKey)
            {
                action = r.Action;
                break;
            }
        }

        return action switch
        {
            RemediationAction.FlattenFolder => FolderFlattener.Flatten(source, destConstraints.MaxFolderDepth),
            RemediationAction.SanitizeFolderName => FolderSanitizer.Sanitize(source, destConstraints),
            _ => source
        };
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~FolderRemediationResolverTests` → expected **PASS** (4 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Remediation src/EMaigrator.Workers.Tests/Remediation
git commit -m "feat(workers): approved-remediation store + pure folder resolver

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: ProviderSessionFactory — build source/dest providers from a migration's connections

**Goal:** Implement an `IProviderSessionFactory` that, given a `MailboxMigrationId`, resolves the right `IProviderPlugin`, decrypts secrets transiently via `ISecretStore`, and constructs an `ISourceProvider`/`IDestinationProvider`, so consumers never touch plugins or secrets directly.

**Files:**
- Create: `src/EMaigrator.Workers/Sessions/IProviderSessionFactory.cs`
- Create: `src/EMaigrator.Workers/Sessions/IMigrationConnectionLookup.cs`
- Create: `src/EMaigrator.Workers/Sessions/MigrationConnections.cs`
- Create: `src/EMaigrator.Workers/Sessions/ProviderSessionFactory.cs`
- Test: `src/EMaigrator.Workers.Tests/Sessions/ProviderSessionFactoryTests.cs`

**Acceptance Criteria:**
- [ ] `IMigrationConnectionLookup.GetAsync(Guid mailboxMigrationId, CancellationToken ct)` returns `MigrationConnections(Guid JobId, string TenantId, ConnectionDescriptor Source, ConnectionDescriptor Dest)`.
- [ ] `IProviderSessionFactory` exposes `Task<ISourceProvider> CreateSourceAsync(Guid mailboxMigrationId, CancellationToken ct)` and `Task<IDestinationProvider> CreateDestinationAsync(Guid mailboxMigrationId, CancellationToken ct)`.
- [ ] Factory selects the plugin whose `Id == descriptor.Provider` from the injected `IEnumerable<IProviderPlugin>`; throws `InvalidOperationException` if none matches.
- [ ] When `descriptor.SecretRef` is non-null, secrets are retrieved via `ISecretStore.RetrieveAsync` and parsed into a `SecretBundle`; when null, an empty `SecretBundle` is passed.
- [ ] The decrypted plaintext is never written to any log (asserted by capturing the test logger output).

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~ProviderSessionFactoryTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Sessions/ProviderSessionFactoryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Sessions;

public sealed class ProviderSessionFactoryTests
{
    private static readonly ProviderId Imap = new("imap");
    private static readonly ProviderId Graph = new("graph");

    private static ConnectionDescriptor Desc(ProviderId p, AuthMethod auth, string? secretRef) => new()
    {
        Provider = p,
        Auth = auth,
        Settings = new Dictionary<string, string> { ["host"] = "mail.example.com" },
        SecretRef = secretRef
    };

    [Fact]
    public async Task Creates_source_from_matching_plugin_and_decrypts_secret()
    {
        var source = Substitute.For<ISourceProvider>();
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(Imap);
        SecretBundle? captured = null;
        plugin.CreateSource(Arg.Any<ConnectionDescriptor>(), Arg.Do<SecretBundle>(b => captured = b))
              .Returns(source);

        var secrets = Substitute.For<ISecretStore>();
        secrets.RetrieveAsync("ref-1", Arg.Any<CancellationToken>())
               .Returns(Task.FromResult("{\"password\":\"hunter2\"}"));

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        var mid = Guid.NewGuid();
        lookup.GetAsync(mid, Arg.Any<CancellationToken>())
              .Returns(new MigrationConnections(Guid.NewGuid(), "tenant-1",
                  Desc(Imap, AuthMethod.ImapBasic, "ref-1"), Desc(Graph, AuthMethod.GraphAppOAuth, null)));

        var factory = new ProviderSessionFactory(new[] { plugin }, secrets, lookup);
        var result = await factory.CreateSourceAsync(mid, CancellationToken.None);

        result.Should().BeSameAs(source);
        captured.Should().NotBeNull();
        captured!.Values.Should().ContainKey("password");
        captured.Values["password"].Should().Be("hunter2");
    }

    [Fact]
    public async Task Destination_with_no_secretref_gets_empty_bundle()
    {
        var dest = Substitute.For<IDestinationProvider>();
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(Graph);
        SecretBundle? captured = null;
        plugin.CreateDestination(Arg.Any<ConnectionDescriptor>(), Arg.Do<SecretBundle>(b => captured = b))
              .Returns(dest);

        var secrets = Substitute.For<ISecretStore>();
        var lookup = Substitute.For<IMigrationConnectionLookup>();
        var mid = Guid.NewGuid();
        lookup.GetAsync(mid, Arg.Any<CancellationToken>())
              .Returns(new MigrationConnections(Guid.NewGuid(), "tenant-1",
                  Desc(Imap, AuthMethod.ImapBasic, "ref-1"), Desc(Graph, AuthMethod.GraphAppOAuth, null)));

        var factory = new ProviderSessionFactory(new[] { plugin }, secrets, lookup);
        var result = await factory.CreateDestinationAsync(mid, CancellationToken.None);

        result.Should().BeSameAs(dest);
        captured!.Values.Should().BeEmpty();
        await secrets.DidNotReceive().RetrieveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_provider_throws()
    {
        var secrets = Substitute.For<ISecretStore>();
        var lookup = Substitute.For<IMigrationConnectionLookup>();
        var mid = Guid.NewGuid();
        lookup.GetAsync(mid, Arg.Any<CancellationToken>())
              .Returns(new MigrationConnections(Guid.NewGuid(), "tenant-1",
                  Desc(Imap, AuthMethod.ImapBasic, null), Desc(Graph, AuthMethod.GraphAppOAuth, null)));

        var factory = new ProviderSessionFactory(Array.Empty<IProviderPlugin>(), secrets, lookup);
        var act = async () => await factory.CreateSourceAsync(mid, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~ProviderSessionFactoryTests` → expected **FAIL**: factory/lookup types do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Sessions/MigrationConnections.cs`:

```csharp
using System;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Workers.Sessions;

public sealed record MigrationConnections(
    Guid JobId,
    string TenantId,
    ConnectionDescriptor Source,
    ConnectionDescriptor Dest);
```

   Implement `src/EMaigrator.Workers/Sessions/IMigrationConnectionLookup.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Sessions;

/// <summary>Resolves a mailbox migration's job, tenant and source/dest connection descriptors (Infrastructure-backed).</summary>
public interface IMigrationConnectionLookup
{
    Task<MigrationConnections> GetAsync(Guid mailboxMigrationId, CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Sessions/IProviderSessionFactory.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Workers.Sessions;

public interface IProviderSessionFactory
{
    Task<ISourceProvider> CreateSourceAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task<IDestinationProvider> CreateDestinationAsync(Guid mailboxMigrationId, CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Sessions/ProviderSessionFactory.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Workers.Sessions;

public sealed class ProviderSessionFactory : IProviderSessionFactory
{
    private readonly IReadOnlyList<IProviderPlugin> _plugins;
    private readonly ISecretStore _secrets;
    private readonly IMigrationConnectionLookup _lookup;

    public ProviderSessionFactory(
        IEnumerable<IProviderPlugin> plugins,
        ISecretStore secrets,
        IMigrationConnectionLookup lookup)
    {
        _plugins = plugins.ToList();
        _secrets = secrets;
        _lookup = lookup;
    }

    public async Task<ISourceProvider> CreateSourceAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        var conns = await _lookup.GetAsync(mailboxMigrationId, ct);
        var plugin = Plugin(conns.Source.Provider);
        var bundle = await ResolveSecretsAsync(conns.Source, ct);
        return plugin.CreateSource(conns.Source, bundle);
    }

    public async Task<IDestinationProvider> CreateDestinationAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        var conns = await _lookup.GetAsync(mailboxMigrationId, ct);
        var plugin = Plugin(conns.Dest.Provider);
        var bundle = await ResolveSecretsAsync(conns.Dest, ct);
        return plugin.CreateDestination(conns.Dest, bundle);
    }

    private IProviderPlugin Plugin(ProviderId id)
        => _plugins.FirstOrDefault(p => p.Id.Value == id.Value)
           ?? throw new InvalidOperationException($"No provider plugin registered for '{id.Value}'.");

    private async Task<SecretBundle> ResolveSecretsAsync(ConnectionDescriptor descriptor, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(descriptor.SecretRef))
            return new SecretBundle(new Dictionary<string, string>());

        // Transient plaintext — never logged (DESIGN.md §10).
        var plaintext = await _secrets.RetrieveAsync(descriptor.SecretRef, ct);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
                     ?? new Dictionary<string, string>();
        return new SecretBundle(values);
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~ProviderSessionFactoryTests` → expected **PASS** (3 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Sessions src/EMaigrator.Workers.Tests/Sessions
git commit -m "feat(workers): provider session factory resolving plugins + transient secrets

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: StreamingMessageCopier — copy one message in-flight without persisting the body

**Goal:** Implement `StreamingMessageCopier.CopyAsync` that, for one `CanonicalMessage`, checks the ledger (skip if done), acquires a destination rate-limit token (back off / penalize on 429), streams `OpenContentAsync`→`WriteMessageAsync`, and checkpoints via `ILedger.MarkAsync` — the idempotent atom, with no body bytes touching disk or the ledger.

**Files:**
- Create: `src/EMaigrator.Workers/Copy/CopyOutcome.cs`
- Create: `src/EMaigrator.Workers/Copy/StreamingMessageCopier.cs`
- Test: `src/EMaigrator.Workers.Tests/Copy/StreamingMessageCopierTests.cs`

**Acceptance Criteria:**
- [ ] `CopyAsync(Guid mailboxMigrationId, RateLimitKey destKey, FolderPath sourceFolder, FolderPath destFolder, CanonicalMessage message, CancellationToken ct)` returns a `CopyOutcome` enum `{ Migrated, Skipped, Throttled, Failed }`.
- [ ] If `ILedger.IsDoneAsync` returns true → returns `Skipped`, and `WriteMessageAsync` is NOT called.
- [ ] If `IRateLimiter.TryAcquireAsync(destKey, 1, ct)` returns false → returns `Throttled` without writing or marking (caller requeues).
- [ ] On successful `WriteResult.Written == true` → calls `ILedger.MarkAsync(..., LedgerStatus.Migrated, null, ct)` and returns `Migrated`.
- [ ] On `WriteResult.Written == false` → calls `ILedger.MarkAsync(..., LedgerStatus.Failed, errorCode, ct)` and returns `Failed`.
- [ ] The content stream obtained from `OpenContentAsync` is disposed (verified) and is never written to any file path; the copier holds no field referencing message bytes.
- [ ] `IdentityKey` passed to ledger calls is `message.IdentityKey` verbatim.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~StreamingMessageCopierTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Copy/StreamingMessageCopierTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Copy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Copy;

public sealed class StreamingMessageCopierTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly RateLimitKey DestKey = new(new ProviderId("graph"), "dest@biz.com");
    private static readonly FolderPath Src = FolderPath.Parse("Inbox");
    private static readonly FolderPath Dst = FolderPath.Parse("Inbox");

    private sealed class TrackedStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public TrackedStream(byte[] data) : base(data) { }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private static CanonicalMessage Msg(string key, TrackedStream stream) => new()
    {
        IdentityKey = key,
        InternalDate = DateTimeOffset.UtcNow,
        SizeBytes = stream.Length,
        OpenContentAsync = _ => Task.FromResult<Stream>(stream)
    };

    private static StreamingMessageCopier Sut(ILedger ledger, IRateLimiter limiter, IDestinationProvider dest)
        => new(ledger, limiter, dest, NullLogger<StreamingMessageCopier>.Instance);

    [Fact]
    public async Task Skips_when_ledger_says_done_without_writing()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:abc", Arg.Any<CancellationToken>()).Returns(true);
        var limiter = Substitute.For<IRateLimiter>();
        var dest = Substitute.For<IDestinationProvider>();
        var stream = new TrackedStream(new byte[] { 1, 2, 3 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:abc", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Skipped);
        await dest.DidNotReceive().WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>());
        await limiter.DidNotReceive().TryAcquireAsync(Arg.Any<RateLimitKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throttled_when_no_token()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:t", Arg.Any<CancellationToken>()).Returns(false);
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(DestKey, 1, Arg.Any<CancellationToken>()).Returns(false);
        var dest = Substitute.For<IDestinationProvider>();
        var stream = new TrackedStream(new byte[] { 1 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:t", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Throttled);
        await dest.DidNotReceive().WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>());
        await ledger.DidNotReceive().MarkAsync(Mid, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LedgerStatus>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Migrated_writes_marks_and_disposes_stream()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:ok", Arg.Any<CancellationToken>()).Returns(false);
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(DestKey, 1, Arg.Any<CancellationToken>()).Returns(true);
        var dest = Substitute.For<IDestinationProvider>();
        dest.WriteMessageAsync(Dst, Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WriteResult(true, "dest-1"));
        var stream = new TrackedStream(new byte[] { 9, 9 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:ok", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Migrated);
        await ledger.Received(1).MarkAsync(Mid, "mid:ok", "Inbox", "Inbox", LedgerStatus.Migrated, null, Arg.Any<CancellationToken>());
        stream.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_write_marks_failed_with_error_code()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:f", Arg.Any<CancellationToken>()).Returns(false);
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(DestKey, 1, Arg.Any<CancellationToken>()).Returns(true);
        var dest = Substitute.For<IDestinationProvider>();
        dest.WriteMessageAsync(Dst, Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WriteResult(false, null, "ErrMessageTooLarge"));
        var stream = new TrackedStream(new byte[] { 1 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:f", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Failed);
        await ledger.Received(1).MarkAsync(Mid, "mid:f", "Inbox", "Inbox", LedgerStatus.Failed, "ErrMessageTooLarge", Arg.Any<CancellationToken>());
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~StreamingMessageCopierTests` → expected **FAIL**: `CopyOutcome`/`StreamingMessageCopier` do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Copy/CopyOutcome.cs`:

```csharp
namespace EMaigrator.Workers.Copy;

public enum CopyOutcome
{
    Migrated,
    Skipped,
    Throttled,
    Failed
}
```

   Implement `src/EMaigrator.Workers/Copy/StreamingMessageCopier.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Copy;

/// <summary>
/// The idempotent atom: copies ONE message source→dest in-flight. Bodies transit memory only —
/// the content stream is opened, handed to the destination, and disposed; never written to disk
/// or persisted (DESIGN.md §6/§10).
/// </summary>
public sealed class StreamingMessageCopier
{
    private readonly ILedger _ledger;
    private readonly IRateLimiter _limiter;
    private readonly IDestinationProvider _dest;
    private readonly ILogger<StreamingMessageCopier> _log;

    public StreamingMessageCopier(
        ILedger ledger,
        IRateLimiter limiter,
        IDestinationProvider dest,
        ILogger<StreamingMessageCopier> log)
    {
        _ledger = ledger;
        _limiter = limiter;
        _dest = dest;
        _log = log;
    }

    public async Task<CopyOutcome> CopyAsync(
        Guid mailboxMigrationId,
        RateLimitKey destKey,
        FolderPath sourceFolder,
        FolderPath destFolder,
        CanonicalMessage message,
        CancellationToken ct)
    {
        // 1) Idempotency check — skip already-done messages (resume / redelivery safe).
        if (await _ledger.IsDoneAsync(mailboxMigrationId, message.IdentityKey, ct))
            return CopyOutcome.Skipped;

        // 2) Pace against the destination's token bucket.
        if (!await _limiter.TryAcquireAsync(destKey, 1, ct))
            return CopyOutcome.Throttled;

        // 3) Stream copy. Open the content stream here and guarantee it is disposed; the bytes
        //    transit memory only — never written to a field, a file, or the ledger (DESIGN.md §6/§10).
        await using var content = await message.OpenContentAsync(ct);
        WriteResult result = await _dest.WriteMessageAsync(destFolder, message, ct);

        var src = sourceFolder.ToString();
        var dst = destFolder.ToString();

        if (result.Written)
        {
            // 4) Checkpoint — per-message (ARCHITECTURE.md §6). No body, only identity + folders + status.
            await _ledger.MarkAsync(mailboxMigrationId, message.IdentityKey, src, dst, LedgerStatus.Migrated, null, ct);
            return CopyOutcome.Migrated;
        }

        await _ledger.MarkAsync(mailboxMigrationId, message.IdentityKey, src, dst, LedgerStatus.Failed, result.ErrorCode, ct);
        _log.LogWarning("Message copy failed mailbox={Mailbox} folder={Folder} error={Error}",
            mailboxMigrationId, dst, result.ErrorCode);
        return CopyOutcome.Failed;
    }
}
```

> Disposal contract: the copier opens the content stream via `message.OpenContentAsync(ct)` and the `await using` guarantees it is disposed whether the write succeeds or throws. The stream is handed to `WriteMessageAsync` (which reads it) and is never copied into a field, buffered to disk, or persisted. The test's `TrackedStream.Disposed` flag proves the copier disposes it deterministically even when the substituted `IDestinationProvider` does not read it. CONTRACTS' "Caller disposes" note refers to whoever opens the stream — here the copier is that caller.

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~StreamingMessageCopierTests` → expected **PASS** (4 tests green; stream disposed by the `await using`).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Copy src/EMaigrator.Workers.Tests/Copy
git commit -m "feat(workers): streaming message copier with ledger checkpoint + rate-limit gate

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: StartMigrationConsumer — fan out folders, apply remediations, publish MigrateFolder

**Goal:** Implement the `StartMigration` consumer that lists source folders, applies approved remediations to compute destination folders, calls `EnsureFolderAsync` on each, and publishes one `MigrateFolder` per folder — honoring cancel (no fan-out if cancelled) and crash-resume (re-enqueue only not-done folders is delegated to ledger-driven skip downstream).

**Files:**
- Create: `src/EMaigrator.Workers/Consumers/StartMigrationConsumer.cs`
- Test: `src/EMaigrator.Workers.Tests/Consumers/StartMigrationConsumerTests.cs`

**Acceptance Criteria:**
- [ ] `StartMigrationConsumer : IConsumer<StartMigration>` (MassTransit) consuming `EMaigrator.Core.Contracts.StartMigration`.
- [ ] On consume: resolves source via `IProviderSessionFactory`, calls `ListFoldersAsync`, resolves each folder's destination via `FolderRemediationResolver` using `IRemediationPlanStore` + the destination provider's `Constraints`, calls `dest.EnsureFolderAsync(destFolder, ct)`, then `context.Publish(new MigrateFolder(mid, folderTaskId, sourceFolder, destFolder))` for each.
- [ ] If `IMigrationControlGate.GetStateAsync` returns `Cancelled` → publishes nothing and returns.
- [ ] One `MigrateFolder` is published per source folder; `SourceFolder`/`DestFolder` strings match the resolved `FolderPath.ToString()`.
- [ ] Verified with the MassTransit in-memory test harness asserting `MigrateFolder` messages were published.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~StartMigrationConsumerTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Consumers/StartMigrationConsumerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class StartMigrationConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    private static (ISourceProvider src, IDestinationProvider dst) Providers()
    {
        var src = Substitute.For<ISourceProvider>();
        src.ListFoldersAsync(Arg.Any<CancellationToken>()).Returns(new List<CanonicalFolder>
        {
            new(FolderPath.Parse("Inbox"), 10),
            new(FolderPath.Parse("A/B/C/D/E"), 5)
        });
        var dst = Substitute.For<IDestinationProvider>();
        dst.Constraints.Returns(new ProviderConstraints { MaxFolderDepth = 3 });
        return (src, dst);
    }

    [Fact]
    public async Task Publishes_one_MigrateFolder_per_folder_with_flatten_applied()
    {
        var (src, dst) = Providers();
        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);

        var plan = Substitute.For<IRemediationPlanStore>();
        plan.GetApprovedAsync(Mid, Arg.Any<CancellationToken>()).Returns(new List<ApprovedRemediation>
        {
            new("A/B/C/D/E", RemediationAction.FlattenFolder)
        });

        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(MigrationControlState.Active);

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string>() }));

        await using var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(plan).AddSingleton(gate).AddSingleton(lookup)
            .AddMassTransitTestHarness(x => x.AddConsumer<StartMigrationConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new StartMigration(Mid));
            (await harness.Consumed.Any<StartMigration>()).Should().BeTrue();

            var published = await harness.Published.SelectAsync<MigrateFolder>().ToListAsync();
            published.Should().HaveCount(2);
            var folders = published.Select(p => p.Context.Message.DestFolder).ToList();
            folders.Should().Contain("Inbox");
            folders.Should().Contain(FolderFlattener.Flatten(FolderPath.Parse("A/B/C/D/E"), 3).ToString());
            await dst.Received(2).EnsureFolderAsync(Arg.Any<FolderPath>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Cancelled_job_publishes_nothing()
    {
        var (src, dst) = Providers();
        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);
        var plan = Substitute.For<IRemediationPlanStore>();
        plan.GetApprovedAsync(Mid, Arg.Any<CancellationToken>()).Returns(new List<ApprovedRemediation>());
        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(MigrationControlState.Cancelled);
        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string>() }));

        await using var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(plan).AddSingleton(gate).AddSingleton(lookup)
            .AddMassTransitTestHarness(x => x.AddConsumer<StartMigrationConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new StartMigration(Mid));
            (await harness.Consumed.Any<StartMigration>()).Should().BeTrue();
            (await harness.Published.SelectAsync<MigrateFolder>().ToListAsync()).Should().BeEmpty();
        }
        finally { await harness.Stop(); }
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~StartMigrationConsumerTests` → expected **FAIL**: `StartMigrationConsumer` does not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Consumers/StartMigrationConsumer.cs`:

```csharp
using System;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Stage 1: fan a mailbox pair out into per-folder tasks. Lists source folders, applies the
/// operator-approved structural remediations to compute each destination folder, ensures the
/// destination folder exists, and publishes one MigrateFolder per folder.
/// </summary>
public sealed class StartMigrationConsumer : IConsumer<StartMigration>
{
    private readonly IProviderSessionFactory _sessions;
    private readonly IRemediationPlanStore _plans;
    private readonly IMigrationControlGate _gate;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly ILogger<StartMigrationConsumer> _log;

    public StartMigrationConsumer(
        IProviderSessionFactory sessions,
        IRemediationPlanStore plans,
        IMigrationControlGate gate,
        IMigrationConnectionLookup lookup,
        ILogger<StartMigrationConsumer> log)
    {
        _sessions = sessions;
        _plans = plans;
        _gate = gate;
        _lookup = lookup;
        _log = log;
    }

    public async Task Consume(ConsumeContext<StartMigration> context)
    {
        var ct = context.CancellationToken;
        var mid = context.Message.MailboxMigrationId;
        var conns = await _lookup.GetAsync(mid, ct);

        var state = await _gate.GetStateAsync(conns.JobId, ct);
        if (state == MigrationControlState.Cancelled)
        {
            _log.LogInformation("StartMigration skipped — job {JobId} cancelled.", conns.JobId);
            return;
        }

        await using var source = await _sessions.CreateSourceAsync(mid, ct);
        await using var dest = await _sessions.CreateDestinationAsync(mid, ct);

        var approved = await _plans.GetApprovedAsync(mid, ct);
        var constraints = dest.Constraints;

        var folders = await source.ListFoldersAsync(ct);
        foreach (var folder in folders)
        {
            var destPath = FolderRemediationResolver.Resolve(folder.Path, approved, constraints);
            await dest.EnsureFolderAsync(destPath, ct);
            await context.Publish(new MigrateFolder(
                mid, Guid.NewGuid(), folder.Path.ToString(), destPath.ToString()));
        }

        _log.LogInformation("StartMigration fanned out {Count} folders for migration {Mid}.", folders.Count, mid);
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~StartMigrationConsumerTests` → expected **PASS** (2 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Consumers/StartMigrationConsumer.cs src/EMaigrator.Workers.Tests/Consumers/StartMigrationConsumerTests.cs
git commit -m "feat(workers): StartMigration consumer fans out folders with approved remediations

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: MigrateFolderConsumer — page source messages into bounded MigrateBatch messages

**Goal:** Implement the `MigrateFolder` consumer that reads message references from the source folder and publishes `MigrateBatch` messages of at most `OrchestrationOptions.BatchSize` refs each — stopping fan-out when the job is paused/cancelled.

**Files:**
- Create: `src/EMaigrator.Workers/Consumers/MigrateFolderConsumer.cs`
- Create: `src/EMaigrator.Workers/Sessions/IMessageRefLister.cs`
- Test: `src/EMaigrator.Workers.Tests/Consumers/MigrateFolderConsumerTests.cs`

**Acceptance Criteria:**
- [ ] `IMessageRefLister.ListRefsAsync(ISourceProvider source, FolderPath folder, CancellationToken ct)` returns `IAsyncEnumerable<string>` of opaque source message refs (so the batch carries refs, not bodies).
- [ ] `MigrateFolderConsumer : IConsumer<MigrateFolder>` reads refs and publishes `MigrateBatch` messages, each with ≤ `BatchSize` refs; `BatchSize` read from injected `IOptions<OrchestrationOptions>`.
- [ ] 250 refs with `BatchSize = 100` → publishes 3 batches (100, 100, 50); all `SourceMessageRefs` round-trip and union to the original 250.
- [ ] Each `MigrateBatch` carries the same `MailboxMigrationId`, `FolderTaskId`, `SourceFolder`, `DestFolder` from the incoming `MigrateFolder`.
- [ ] If the gate reports `Paused` or `Cancelled` for the job → publishes nothing.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateFolderConsumerTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Consumers/MigrateFolderConsumerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class MigrateFolderConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FolderTaskId = Guid.NewGuid();

    private static async IAsyncEnumerable<string> Refs(int n, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < n; i++) { ct.ThrowIfCancellationRequested(); yield return $"ref-{i}"; await Task.Yield(); }
    }

    private static ServiceProvider Build(MigrationControlState state, int refCount, out ITestHarness _)
    {
        var src = Substitute.For<ISourceProvider>();
        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);

        var lister = Substitute.For<IMessageRefLister>();
        lister.ListRefsAsync(src, Arg.Any<FolderPath>(), Arg.Any<CancellationToken>()).Returns(Refs(refCount));

        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(state);

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string>() }));

        var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(lister).AddSingleton(gate).AddSingleton(lookup)
            .AddSingleton<IOptions<OrchestrationOptions>>(Options.Create(new OrchestrationOptions { BatchSize = 100 }))
            .AddMassTransitTestHarness(x => x.AddConsumer<MigrateFolderConsumer>())
            .BuildServiceProvider(true);
        _ = provider.GetRequiredService<ITestHarness>();
        return provider;
    }

    [Fact]
    public async Task Pages_into_batches_of_batchsize()
    {
        await using var provider = Build(MigrationControlState.Active, 250, out _);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateFolder(Mid, FolderTaskId, "Inbox", "Inbox"));
            (await harness.Consumed.Any<MigrateFolder>()).Should().BeTrue();

            var batches = (await harness.Published.SelectAsync<MigrateBatch>().ToListAsync())
                .Select(p => p.Context.Message).ToList();
            batches.Should().HaveCount(3);
            batches.Select(b => b.SourceMessageRefs.Count).Should().BeEquivalentTo(new[] { 100, 100, 50 });
            batches.SelectMany(b => b.SourceMessageRefs).Distinct().Should().HaveCount(250);
            batches.Should().OnlyContain(b => b.MailboxMigrationId == Mid && b.FolderTaskId == FolderTaskId
                && b.SourceFolder == "Inbox" && b.DestFolder == "Inbox");
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Paused_job_publishes_no_batches()
    {
        await using var provider = Build(MigrationControlState.Paused, 250, out _);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateFolder(Mid, FolderTaskId, "Inbox", "Inbox"));
            (await harness.Consumed.Any<MigrateFolder>()).Should().BeTrue();
            (await harness.Published.SelectAsync<MigrateBatch>().ToListAsync()).Should().BeEmpty();
        }
        finally { await harness.Stop(); }
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateFolderConsumerTests` → expected **FAIL**: `IMessageRefLister`/`MigrateFolderConsumer` do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Sessions/IMessageRefLister.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Sessions;

/// <summary>
/// Enumerates opaque source message references for a folder (UIDs / Graph ids / Gmail ids).
/// Batches carry refs — never bodies — so a queued batch holds no message content.
/// </summary>
public interface IMessageRefLister
{
    IAsyncEnumerable<string> ListRefsAsync(ISourceProvider source, FolderPath folder, CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Consumers/MigrateFolderConsumer.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Stage 2: page a folder's source messages into bounded MigrateBatch messages (BatchSize from
/// OrchestrationOptions). Stops fanning out if the job is paused or cancelled.
/// </summary>
public sealed class MigrateFolderConsumer : IConsumer<MigrateFolder>
{
    private readonly IProviderSessionFactory _sessions;
    private readonly IMessageRefLister _lister;
    private readonly IMigrationControlGate _gate;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly OrchestrationOptions _options;
    private readonly ILogger<MigrateFolderConsumer> _log;

    public MigrateFolderConsumer(
        IProviderSessionFactory sessions,
        IMessageRefLister lister,
        IMigrationControlGate gate,
        IMigrationConnectionLookup lookup,
        IOptions<OrchestrationOptions> options,
        ILogger<MigrateFolderConsumer> log)
    {
        _sessions = sessions;
        _lister = lister;
        _gate = gate;
        _lookup = lookup;
        _options = options.Value;
        _log = log;
    }

    public async Task Consume(ConsumeContext<MigrateFolder> context)
    {
        var ct = context.CancellationToken;
        var msg = context.Message;
        var conns = await _lookup.GetAsync(msg.MailboxMigrationId, ct);

        var state = await _gate.GetStateAsync(conns.JobId, ct);
        if (state != MigrationControlState.Active)
        {
            _log.LogInformation("MigrateFolder halted — job {JobId} is {State}.", conns.JobId, state);
            return;
        }

        await using var source = await _sessions.CreateSourceAsync(msg.MailboxMigrationId, ct);
        var folder = FolderPath.Parse(msg.SourceFolder);

        var buffer = new List<string>(_options.BatchSize);
        await foreach (var reference in _lister.ListRefsAsync(source, folder, ct))
        {
            buffer.Add(reference);
            if (buffer.Count >= _options.BatchSize)
            {
                await PublishBatchAsync(context, msg, buffer);
                buffer = new List<string>(_options.BatchSize);
            }
        }
        if (buffer.Count > 0)
            await PublishBatchAsync(context, msg, buffer);
    }

    private static Task PublishBatchAsync(ConsumeContext context, MigrateFolder src, List<string> refs)
        => context.Publish(new MigrateBatch(
            src.MailboxMigrationId, src.FolderTaskId, src.SourceFolder, src.DestFolder, refs.ToArray()));
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateFolderConsumerTests` → expected **PASS** (2 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Consumers/MigrateFolderConsumer.cs src/EMaigrator.Workers/Sessions/IMessageRefLister.cs src/EMaigrator.Workers.Tests/Consumers/MigrateFolderConsumerTests.cs
git commit -m "feat(workers): MigrateFolder consumer pages source refs into bounded batches

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: MigrateBatchConsumer — per-message copy, throttle requeue, 429 penalize, progress event

**Goal:** Implement the `MigrateBatch` consumer that hydrates each ref into a `CanonicalMessage`, runs `StreamingMessageCopier.CopyAsync`, requeues the whole batch (throwing for redelivery) on first `Throttled` after `IRateLimiter.PenalizeAsync`, and publishes a `MigrationProgressEvent` with live counts on completion.

**Files:**
- Create: `src/EMaigrator.Workers/Sessions/IMessageHydrator.cs`
- Create: `src/EMaigrator.Workers/Consumers/MigrateBatchConsumer.cs`
- Create: `src/EMaigrator.Workers/Consumers/ThrottledRequeueException.cs`
- Test: `src/EMaigrator.Workers.Tests/Consumers/MigrateBatchConsumerTests.cs`

**Acceptance Criteria:**
- [ ] `IMessageHydrator.HydrateAsync(ISourceProvider source, FolderPath folder, string reference, CancellationToken ct)` returns a `CanonicalMessage`.
- [ ] `MigrateBatchConsumer : IConsumer<MigrateBatch>` copies each ref via `StreamingMessageCopier`; rate-limit key is `(destProvider, destMailbox)`.
- [ ] On a `Throttled` outcome: calls `IRateLimiter.PenalizeAsync(destKey, retryAfter, ct)` then throws `ThrottledRequeueException` so MassTransit redelivers the un-acked batch (no progress event with partial loss; ledger already skips the messages copied before the throttle).
- [ ] If the gate is `Paused`/`Cancelled` at batch start → returns without copying (drain semantics).
- [ ] On full batch completion publishes one `MigrationProgressEvent(mid, migrated, total, currentFolder, msgPerMin, status)` where `migrated`/`total` come from `ILedger.GetCountsAsync` and `status` is `"Running"`.
- [ ] Already-done messages (ledger skip) do not double-count.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateBatchConsumerTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Consumers/MigrateBatchConsumerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class MigrateBatchConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FolderTaskId = Guid.NewGuid();

    private static CanonicalMessage Msg(string key) => new()
    {
        IdentityKey = key,
        InternalDate = DateTimeOffset.UtcNow,
        SizeBytes = 3,
        OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }))
    };

    private static (ServiceProvider provider, ILedger ledger, IRateLimiter limiter) Build(
        MigrationControlState state, bool throttleSecond)
    {
        var src = Substitute.For<ISourceProvider>();
        var dst = Substitute.For<IDestinationProvider>();
        dst.Id.Returns(new ProviderId("graph"));
        dst.WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
           .Returns(new WriteResult(true, "d"));

        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);

        var hydrator = Substitute.For<IMessageHydrator>();
        hydrator.HydrateAsync(src, Arg.Any<FolderPath>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Msg((string)ci[2]));

        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        ledger.GetCountsAsync(Mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(2, 0, 0, 0));

        var limiter = Substitute.For<IRateLimiter>();
        if (throttleSecond)
        {
            var calls = 0;
            limiter.TryAcquireAsync(Arg.Any<RateLimitKey>(), 1, Arg.Any<CancellationToken>())
                   .Returns(_ => Task.FromResult(++calls <= 1)); // first ok, then throttled
        }
        else
        {
            limiter.TryAcquireAsync(Arg.Any<RateLimitKey>(), 1, Arg.Any<CancellationToken>()).Returns(true);
        }

        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(state);

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string> { ["accountEmail"] = "dest@biz.com" } }));

        var copierFactory = new StreamingCopierFactory(ledger, limiter);

        var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(hydrator).AddSingleton(gate).AddSingleton(lookup)
            .AddSingleton(ledger).AddSingleton(limiter).AddSingleton(copierFactory)
            .AddMassTransitTestHarness(x => x.AddConsumer<MigrateBatchConsumer>())
            .BuildServiceProvider(true);
        return (provider, ledger, limiter);
    }

    [Fact]
    public async Task Copies_all_and_publishes_progress()
    {
        var (provider, ledger, _) = Build(MigrationControlState.Active, throttleSecond: false);
        await using var _p = provider;
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateBatch(Mid, FolderTaskId, "Inbox", "Inbox", new[] { "ref-1", "ref-2" }));
            (await harness.Consumed.Any<MigrateBatch>()).Should().BeTrue();
            var progress = (await harness.Published.SelectAsync<MigrationProgressEvent>().ToListAsync())
                .Select(p => p.Context.Message).Single();
            progress.Migrated.Should().Be(2);
            progress.Status.Should().Be("Running");
            await ledger.Received(2).MarkAsync(Mid, Arg.Any<string>(), "Inbox", "Inbox", LedgerStatus.Migrated, null, Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Throttle_penalizes_and_faults_for_redelivery()
    {
        var (provider, _, limiter) = Build(MigrationControlState.Active, throttleSecond: true);
        await using var _p = provider;
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateBatch(Mid, FolderTaskId, "Inbox", "Inbox", new[] { "ref-1", "ref-2" }));
            var consumed = await harness.Consumed.SelectAsync<MigrateBatch>().FirstOrDefault();
            consumed.Should().NotBeNull();
            consumed!.Exception.Should().BeOfType<ThrottledRequeueException>();
            await limiter.Received().PenalizeAsync(Arg.Any<RateLimitKey>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Paused_at_start_copies_nothing()
    {
        var (provider, ledger, _) = Build(MigrationControlState.Paused, throttleSecond: false);
        await using var _p = provider;
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateBatch(Mid, FolderTaskId, "Inbox", "Inbox", new[] { "ref-1" }));
            (await harness.Consumed.Any<MigrateBatch>()).Should().BeTrue();
            await ledger.DidNotReceive().MarkAsync(Mid, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LedgerStatus>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }
}
```

> The test references a small `StreamingCopierFactory` helper that builds a `StreamingMessageCopier` bound to a specific destination provider per batch. Add it as part of the implementation below.

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateBatchConsumerTests` → expected **FAIL**: `IMessageHydrator`, `MigrateBatchConsumer`, `ThrottledRequeueException`, `StreamingCopierFactory` do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Sessions/IMessageHydrator.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Sessions;

/// <summary>Materializes one opaque source ref into a CanonicalMessage (whose body opens as a stream on demand).</summary>
public interface IMessageHydrator
{
    Task<CanonicalMessage> HydrateAsync(ISourceProvider source, FolderPath folder, string reference, CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Consumers/ThrottledRequeueException.cs`:

```csharp
using System;

namespace EMaigrator.Workers.Consumers;

/// <summary>Thrown to fault a batch so MassTransit redelivers it after a provider throttle (429).</summary>
public sealed class ThrottledRequeueException : Exception
{
    public ThrottledRequeueException(string message) : base(message) { }
}
```

   Implement `src/EMaigrator.Workers/Copy/StreamingCopierFactory.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMaigrator.Workers.Copy;

/// <summary>Builds a StreamingMessageCopier bound to the destination provider resolved per batch.</summary>
public sealed class StreamingCopierFactory
{
    private readonly ILedger _ledger;
    private readonly IRateLimiter _limiter;

    public StreamingCopierFactory(ILedger ledger, IRateLimiter limiter)
    {
        _ledger = ledger;
        _limiter = limiter;
    }

    public StreamingMessageCopier For(IDestinationProvider dest)
        => new(_ledger, _limiter, dest, NullLogger<StreamingMessageCopier>.Instance);
}
```

   Implement `src/EMaigrator.Workers/Consumers/MigrateBatchConsumer.cs`:

```csharp
using System;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Stage 3: copy each message in a batch (the idempotent atom). Ledger skips done messages,
/// the rate limiter paces writes; a throttle penalizes the bucket and faults the batch for
/// redelivery. Publishes a live progress event on completion.
/// </summary>
public sealed class MigrateBatchConsumer : IConsumer<MigrateBatch>
{
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(10);

    private readonly IProviderSessionFactory _sessions;
    private readonly IMessageHydrator _hydrator;
    private readonly IMigrationControlGate _gate;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly ILedger _ledger;
    private readonly IRateLimiter _limiter;
    private readonly StreamingCopierFactory _copierFactory;
    private readonly ILogger<MigrateBatchConsumer> _log;

    public MigrateBatchConsumer(
        IProviderSessionFactory sessions,
        IMessageHydrator hydrator,
        IMigrationControlGate gate,
        IMigrationConnectionLookup lookup,
        ILedger ledger,
        IRateLimiter limiter,
        StreamingCopierFactory copierFactory,
        ILogger<MigrateBatchConsumer> log)
    {
        _sessions = sessions;
        _hydrator = hydrator;
        _gate = gate;
        _lookup = lookup;
        _ledger = ledger;
        _limiter = limiter;
        _copierFactory = copierFactory;
        _log = log;
    }

    public async Task Consume(ConsumeContext<MigrateBatch> context)
    {
        var ct = context.CancellationToken;
        var msg = context.Message;
        var conns = await _lookup.GetAsync(msg.MailboxMigrationId, ct);

        var state = await _gate.GetStateAsync(conns.JobId, ct);
        if (state != MigrationControlState.Active)
        {
            _log.LogInformation("MigrateBatch drained — job {JobId} is {State}.", conns.JobId, state);
            return;
        }

        await using var source = await _sessions.CreateSourceAsync(msg.MailboxMigrationId, ct);
        await using var dest = await _sessions.CreateDestinationAsync(msg.MailboxMigrationId, ct);

        var destAccount = conns.Dest.Settings.TryGetValue("accountEmail", out var acct) ? acct : msg.DestFolder;
        var destKey = new RateLimitKey(dest.Id, destAccount);
        var sourceFolder = FolderPath.Parse(msg.SourceFolder);
        var destFolder = FolderPath.Parse(msg.DestFolder);
        var copier = _copierFactory.For(dest);

        foreach (var reference in msg.SourceMessageRefs)
        {
            var message = await _hydrator.HydrateAsync(source, sourceFolder, reference, ct);
            var outcome = await copier.CopyAsync(msg.MailboxMigrationId, destKey, sourceFolder, destFolder, message, ct);
            if (outcome == CopyOutcome.Throttled)
            {
                await _limiter.PenalizeAsync(destKey, DefaultRetryAfter, ct);
                throw new ThrottledRequeueException(
                    $"Throttled on {destKey.Provider.Value}:{destKey.Account}; requeuing batch for redelivery.");
            }
        }

        var counts = await _ledger.GetCountsAsync(msg.MailboxMigrationId, ct);
        var total = counts.Migrated + counts.Skipped + counts.Failed + counts.Pending;
        await context.Publish(new MigrationProgressEvent(
            msg.MailboxMigrationId, counts.Migrated, total, msg.DestFolder, 0d, "Running"));
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateBatchConsumerTests` → expected **PASS** (3 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Consumers/MigrateBatchConsumer.cs src/EMaigrator.Workers/Consumers/ThrottledRequeueException.cs src/EMaigrator.Workers/Sessions/IMessageHydrator.cs src/EMaigrator.Workers/Copy/StreamingCopierFactory.cs src/EMaigrator.Workers.Tests/Consumers/MigrateBatchConsumerTests.cs
git commit -m "feat(workers): MigrateBatch consumer with idempotent copy, throttle requeue, progress

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: PoisonMessageConsumer (DLQ) — content-free NeedsDecisionEvent, never wedge the folder

**Goal:** Implement the dead-letter/fault consumer that, when a `MigrateBatch` exhausts retries, records a `NeedsDecisionEvent` carrying only identity keys + error (no body/subject) and marks the affected messages `Failed` in the ledger — so one poison message never blocks its folder or mailbox.

**Files:**
- Create: `src/EMaigrator.Workers/Consumers/MigrateBatchFaultConsumer.cs`
- Test: `src/EMaigrator.Workers.Tests/Consumers/MigrateBatchFaultConsumerTests.cs`

**Acceptance Criteria:**
- [ ] `MigrateBatchFaultConsumer : IConsumer<Fault<MigrateBatch>>` (MassTransit fault contract for poison `MigrateBatch`).
- [ ] On consume it publishes one `NeedsDecisionEvent(mid, "PoisonBatch", detail, options)` where `detail` contains the source folder + the batch's message refs + the exception type name, and contains **no** message body/subject text.
- [ ] `options` is `[ RemediationAction.SkipMessage ]`.
- [ ] It marks each ref's identity in the ledger as `Failed` with an error code derived from the fault (the consumer does NOT re-attempt the copy).
- [ ] The processed folder/mailbox is not blocked — the fault consumer returns normally (acks the fault), so subsequent batches keep flowing.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateBatchFaultConsumerTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Consumers/MigrateBatchFaultConsumerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Workers.Consumers;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class MigrateBatchFaultConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();

    [Fact]
    public async Task Fault_records_content_free_needs_decision_and_marks_failed()
    {
        var ledger = Substitute.For<ILedger>();

        await using var provider = new ServiceCollection()
            .AddSingleton(ledger)
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<MigrateBatchFaultConsumer>();
                x.AddConsumer<CollectingNeedsDecisionConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var poison = new MigrateBatch(Mid, Guid.NewGuid(), "Inbox", "Inbox", new[] { "h:aaa", "h:bbb" });
            // Simulate MassTransit producing a Fault<MigrateBatch> after retries are exhausted.
            await harness.Bus.Publish<Fault<MigrateBatch>>(new
            {
                Message = poison,
                Exceptions = new[] { new ExceptionInfoStub() },
                FaultId = Guid.NewGuid(),
                FaultedMessageId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow
            });

            (await harness.Consumed.Any<Fault<MigrateBatch>>()).Should().BeTrue();

            var nd = (await harness.Published.SelectAsync<NeedsDecisionEvent>().ToListAsync())
                .Select(p => p.Context.Message).Single();
            nd.MailboxMigrationId.Should().Be(Mid);
            nd.IssueType.Should().Be("PoisonBatch");
            nd.Options.Should().BeEquivalentTo(new[] { RemediationAction.SkipMessage });
            nd.Detail.Should().Contain("h:aaa").And.Contain("h:bbb").And.Contain("Inbox");
            // Content-free: no body/subject markers leak into the event.
            nd.Detail.ToLowerInvariant().Should().NotContain("body").And.NotContain("subject:");

            await ledger.Received().MarkAsync(Mid, "h:aaa", "Inbox", "Inbox", LedgerStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await ledger.Received().MarkAsync(Mid, "h:bbb", "Inbox", "Inbox", LedgerStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    // Minimal stub so the dynamic Fault<> proxy has an ExceptionInfo with a usable type name.
    public sealed class ExceptionInfoStub : ExceptionInfo
    {
        public string ExceptionType => "EMaigrator.Workers.Tests.PoisonException";
        public ExceptionInfo? InnerException => null;
        public string StackTrace => "";
        public string Message => "message too large";
        public string Source => "test";
        public Dictionary<string, object?> Data => new();
    }

    // Collector to keep the bus topology valid for the NeedsDecisionEvent publish.
    public sealed class CollectingNeedsDecisionConsumer : IConsumer<NeedsDecisionEvent>
    {
        public Task Consume(ConsumeContext<NeedsDecisionEvent> context) => Task.CompletedTask;
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateBatchFaultConsumerTests` → expected **FAIL**: `MigrateBatchFaultConsumer` does not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Consumers/MigrateBatchFaultConsumer.cs`:

```csharp
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// DLQ handler: when a MigrateBatch exhausts retries, MassTransit produces Fault&lt;MigrateBatch&gt;.
/// We record a content-free NeedsDecisionEvent (identity keys + folder + error type only — NO body,
/// NO subject) and mark the affected messages Failed. One poison message never wedges the folder
/// (DESIGN.md §8 / ARCHITECTURE.md §7).
/// </summary>
public sealed class MigrateBatchFaultConsumer : IConsumer<Fault<MigrateBatch>>
{
    private readonly ILedger _ledger;
    private readonly ILogger<MigrateBatchFaultConsumer> _log;

    public MigrateBatchFaultConsumer(ILedger ledger, ILogger<MigrateBatchFaultConsumer> log)
    {
        _ledger = ledger;
        _log = log;
    }

    public async Task Consume(ConsumeContext<Fault<MigrateBatch>> context)
    {
        var ct = context.CancellationToken;
        var batch = context.Message.Message;
        var ex = context.Message.Exceptions?.FirstOrDefault();
        var errorType = ex?.ExceptionType ?? "UnknownFault";
        var errorCode = ShortCode(errorType);

        var sb = new StringBuilder();
        sb.Append("folder=").Append(batch.SourceFolder)
          .Append("; errorType=").Append(errorType)
          .Append("; refs=").Append(string.Join(",", batch.SourceMessageRefs));
        var detail = sb.ToString();

        await context.Publish(new NeedsDecisionEvent(
            batch.MailboxMigrationId, "PoisonBatch", detail, new[] { RemediationAction.SkipMessage }));

        foreach (var reference in batch.SourceMessageRefs)
        {
            await _ledger.MarkAsync(batch.MailboxMigrationId, reference,
                batch.SourceFolder, batch.DestFolder, LedgerStatus.Failed, errorCode, ct);
        }

        _log.LogWarning("Poison batch parked: mailbox={Mid} folder={Folder} refs={Count} error={Error}",
            batch.MailboxMigrationId, batch.SourceFolder, batch.SourceMessageRefs.Count, errorType);
    }

    private static string ShortCode(string exceptionType)
    {
        var name = exceptionType.Split('.').Last();
        return name.EndsWith("Exception", StringComparison.Ordinal)
            ? name[..^"Exception".Length]
            : name;
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~MigrateBatchFaultConsumerTests` → expected **PASS** (1 test green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Consumers/MigrateBatchFaultConsumer.cs src/EMaigrator.Workers.Tests/Consumers/MigrateBatchFaultConsumerTests.cs
git commit -m "feat(workers): DLQ fault consumer emits content-free needs-decision events

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: MassTransitJobOrchestrator + control consumers — pause/resume/cancel/resume-not-done

**Goal:** Implement `IJobOrchestrator` (MassTransit-backed) and the control consumers so the API can pause/resume/cancel a job and re-enqueue not-done items; resume re-publishes `StartMigration` (which fans out again — ledger skip makes it idempotent), and cancel/pause flip the control gate.

**Files:**
- Create: `src/EMaigrator.Workers/Orchestration/MassTransitJobOrchestrator.cs`
- Create: `src/EMaigrator.Workers/Orchestration/ControlMessages.cs`
- Create: `src/EMaigrator.Workers/Consumers/JobControlConsumer.cs`
- Create: `src/EMaigrator.Workers/Orchestration/IJobMigrationLookup.cs`
- Test: `src/EMaigrator.Workers.Tests/Orchestration/JobControlTests.cs`

**Acceptance Criteria:**
- [ ] `MassTransitJobOrchestrator : IJobOrchestrator` implements `EnqueueMigrationAsync` (publish `StartMigration`), `RequestPauseAsync`/`RequestResumeAsync`/`RequestCancelAsync` (publish `PauseJob`/`ResumeJob`/`CancelJob` internal control messages keyed by `jobId`).
- [ ] `JobControlConsumer` consumes `PauseJob`→`gate.PauseAsync`, `CancelJob`→`gate.CancelAsync`, and `ResumeJob`→`gate.ResumeAsync` then re-publishes `StartMigration` for each not-done `MailboxMigrationId` of the job (looked up via `IJobMigrationLookup`).
- [ ] Internal control messages (`PauseJob`/`ResumeJob`/`CancelJob`) live in `EMaigrator.Workers.Orchestration` (not the frozen Core contracts) and carry only `Guid JobId`.
- [ ] `EnqueueMigrationAsync` publishes exactly one `StartMigration(mailboxMigrationId)`.
- [ ] Resume re-enqueues `StartMigration` once per migration returned by `IJobMigrationLookup.GetNotDoneMigrationsAsync(jobId, ct)`.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~JobControlTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Orchestration/JobControlTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Orchestration;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Orchestration;

public sealed class JobControlTests
{
    [Fact]
    public async Task Enqueue_publishes_single_start_migration()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orch = new MassTransitJobOrchestrator(harness.Bus);
            var mid = Guid.NewGuid();
            await orch.EnqueueMigrationAsync(mid, CancellationToken.None);
            var published = await harness.Published.SelectAsync<StartMigration>().ToListAsync();
            published.Should().ContainSingle(p => p.Context.Message.MailboxMigrationId == mid);
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Cancel_flips_gate_to_cancelled()
    {
        var gate = Substitute.For<IMigrationControlGate>();
        var lookup = Substitute.For<IJobMigrationLookup>();
        var jobId = Guid.NewGuid();

        await using var provider = new ServiceCollection()
            .AddSingleton(gate).AddSingleton(lookup)
            .AddMassTransitTestHarness(x => x.AddConsumer<JobControlConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orch = new MassTransitJobOrchestrator(harness.Bus);
            await orch.RequestCancelAsync(jobId, CancellationToken.None);
            (await harness.Consumed.Any<CancelJob>()).Should().BeTrue();
            await gate.Received().CancelAsync(jobId, Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Resume_reenqueues_start_for_each_not_done_migration()
    {
        var gate = Substitute.For<IMigrationControlGate>();
        var lookup = Substitute.For<IJobMigrationLookup>();
        var jobId = Guid.NewGuid();
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        lookup.GetNotDoneMigrationsAsync(jobId, Arg.Any<CancellationToken>())
              .Returns(new List<Guid> { m1, m2 });

        await using var provider = new ServiceCollection()
            .AddSingleton(gate).AddSingleton(lookup)
            .AddMassTransitTestHarness(x => x.AddConsumer<JobControlConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orch = new MassTransitJobOrchestrator(harness.Bus);
            await orch.RequestResumeAsync(jobId, CancellationToken.None);
            (await harness.Consumed.Any<ResumeJob>()).Should().BeTrue();
            await gate.Received().ResumeAsync(jobId, Arg.Any<CancellationToken>());
            var starts = (await harness.Published.SelectAsync<StartMigration>().ToListAsync())
                .Select(p => p.Context.Message.MailboxMigrationId).ToList();
            starts.Should().BeEquivalentTo(new[] { m1, m2 });
        }
        finally { await harness.Stop(); }
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~JobControlTests` → expected **FAIL**: orchestrator/control types do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Orchestration/ControlMessages.cs`:

```csharp
using System;

namespace EMaigrator.Workers.Orchestration;

// Internal control messages (NOT frozen Core contracts) — carry only a JobId.
public sealed record PauseJob(Guid JobId);
public sealed record ResumeJob(Guid JobId);
public sealed record CancelJob(Guid JobId);
```

   Implement `src/EMaigrator.Workers/Orchestration/IJobMigrationLookup.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Orchestration;

/// <summary>Lists the mailbox migrations of a job whose ledger still has not-done items (resume target).</summary>
public interface IJobMigrationLookup
{
    Task<IReadOnlyList<Guid>> GetNotDoneMigrationsAsync(Guid jobId, CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Orchestration/MassTransitJobOrchestrator.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Workers.Orchestration;

public sealed class MassTransitJobOrchestrator : IJobOrchestrator
{
    private readonly IPublishEndpoint _publish;

    public MassTransitJobOrchestrator(IPublishEndpoint publish) => _publish = publish;

    public Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct)
        => _publish.Publish(new StartMigration(mailboxMigrationId), ct);

    public Task RequestPauseAsync(Guid jobId, CancellationToken ct)
        => _publish.Publish(new PauseJob(jobId), ct);

    public Task RequestResumeAsync(Guid jobId, CancellationToken ct)
        => _publish.Publish(new ResumeJob(jobId), ct);

    public Task RequestCancelAsync(Guid jobId, CancellationToken ct)
        => _publish.Publish(new CancelJob(jobId), ct);
}
```

   Implement `src/EMaigrator.Workers/Consumers/JobControlConsumer.cs`:

```csharp
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Orchestration;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Applies job-level control. Pause/Cancel flip the distributed gate (workers drain).
/// Resume clears the gate and re-enqueues StartMigration for every not-done migration of the job
/// (crash/pause/deploy resume = re-enqueue not-done — DESIGN.md §6, ARCHITECTURE.md §6).
/// </summary>
public sealed class JobControlConsumer :
    IConsumer<PauseJob>,
    IConsumer<ResumeJob>,
    IConsumer<CancelJob>
{
    private readonly IMigrationControlGate _gate;
    private readonly IJobMigrationLookup _lookup;
    private readonly ILogger<JobControlConsumer> _log;

    public JobControlConsumer(IMigrationControlGate gate, IJobMigrationLookup lookup, ILogger<JobControlConsumer> log)
    {
        _gate = gate;
        _lookup = lookup;
        _log = log;
    }

    public async Task Consume(ConsumeContext<PauseJob> context)
    {
        await _gate.PauseAsync(context.Message.JobId, context.CancellationToken);
        _log.LogInformation("Job {JobId} paused.", context.Message.JobId);
    }

    public async Task Consume(ConsumeContext<CancelJob> context)
    {
        await _gate.CancelAsync(context.Message.JobId, context.CancellationToken);
        _log.LogInformation("Job {JobId} cancelled.", context.Message.JobId);
    }

    public async Task Consume(ConsumeContext<ResumeJob> context)
    {
        var ct = context.CancellationToken;
        var jobId = context.Message.JobId;
        await _gate.ResumeAsync(jobId, ct);

        var migrations = await _lookup.GetNotDoneMigrationsAsync(jobId, ct);
        foreach (var mid in migrations)
            await context.Publish(new StartMigration(mid));

        _log.LogInformation("Job {JobId} resumed; re-enqueued {Count} migrations.", jobId, migrations.Count);
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~JobControlTests` → expected **PASS** (3 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Orchestration src/EMaigrator.Workers/Consumers/JobControlConsumer.cs src/EMaigrator.Workers.Tests/Orchestration/JobControlTests.cs
git commit -m "feat(workers): MassTransit job orchestrator + pause/resume/cancel control consumer

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: CrashResumeStartupService — re-enqueue not-done migrations on worker startup

**Goal:** Implement an `IHostedService` that, on worker startup, scans for jobs left `Running` (interrupted by crash/deploy) and re-enqueues `StartMigration` for each of their not-done migrations — so an interrupted run self-heals when the worker comes back.

**Files:**
- Create: `src/EMaigrator.Workers/Startup/CrashResumeStartupService.cs`
- Create: `src/EMaigrator.Workers/Startup/IInterruptedJobLookup.cs`
- Test: `src/EMaigrator.Workers.Tests/Startup/CrashResumeStartupServiceTests.cs`

**Acceptance Criteria:**
- [ ] `IInterruptedJobLookup.GetRunningMigrationsToResumeAsync(CancellationToken ct)` returns `IReadOnlyList<Guid>` of not-done `MailboxMigrationId`s belonging to jobs in `Running` state.
- [ ] `CrashResumeStartupService : IHostedService` on `StartAsync` enqueues `StartMigration` (via `IJobOrchestrator.EnqueueMigrationAsync`) for each returned migration.
- [ ] `StopAsync` is a no-op returning `Task.CompletedTask`.
- [ ] When the lookup returns empty, nothing is enqueued.
- [ ] Each migration is enqueued exactly once.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~CrashResumeStartupServiceTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/Startup/CrashResumeStartupServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Workers.Startup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Startup;

public sealed class CrashResumeStartupServiceTests
{
    [Fact]
    public async Task Reenqueues_each_not_done_running_migration_once()
    {
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var lookup = Substitute.For<IInterruptedJobLookup>();
        lookup.GetRunningMigrationsToResumeAsync(Arg.Any<CancellationToken>())
              .Returns(new List<Guid> { m1, m2 });
        var orch = Substitute.For<IJobOrchestrator>();

        var svc = new CrashResumeStartupService(lookup, orch, NullLogger<CrashResumeStartupService>.Instance);
        await svc.StartAsync(CancellationToken.None);

        await orch.Received(1).EnqueueMigrationAsync(m1, Arg.Any<CancellationToken>());
        await orch.Received(1).EnqueueMigrationAsync(m2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_running_migrations_enqueues_nothing()
    {
        var lookup = Substitute.For<IInterruptedJobLookup>();
        lookup.GetRunningMigrationsToResumeAsync(Arg.Any<CancellationToken>())
              .Returns(new List<Guid>());
        var orch = Substitute.For<IJobOrchestrator>();

        var svc = new CrashResumeStartupService(lookup, orch, NullLogger<CrashResumeStartupService>.Instance);
        await svc.StartAsync(CancellationToken.None);

        await orch.DidNotReceive().EnqueueMigrationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await svc.StopAsync(CancellationToken.None); // no-op completes
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~CrashResumeStartupServiceTests` → expected **FAIL**: startup types do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers/Startup/IInterruptedJobLookup.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Startup;

/// <summary>Finds mailbox migrations of jobs left in Running state (crash/deploy interrupted) that still have not-done ledger items.</summary>
public interface IInterruptedJobLookup
{
    Task<IReadOnlyList<Guid>> GetRunningMigrationsToResumeAsync(CancellationToken ct);
}
```

   Implement `src/EMaigrator.Workers/Startup/CrashResumeStartupService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Startup;

/// <summary>
/// On worker startup, re-enqueue StartMigration for every not-done migration of a Running job.
/// Resume = scan ledger for not-done items, re-enqueue (DESIGN.md §6 / ARCHITECTURE.md §6). The
/// ledger's IsDone check makes re-fan-out idempotent — already-copied messages are skipped.
/// </summary>
public sealed class CrashResumeStartupService : IHostedService
{
    private readonly IInterruptedJobLookup _lookup;
    private readonly IJobOrchestrator _orchestrator;
    private readonly ILogger<CrashResumeStartupService> _log;

    public CrashResumeStartupService(
        IInterruptedJobLookup lookup,
        IJobOrchestrator orchestrator,
        ILogger<CrashResumeStartupService> log)
    {
        _lookup = lookup;
        _orchestrator = orchestrator;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var migrations = await _lookup.GetRunningMigrationsToResumeAsync(cancellationToken);
        foreach (var mid in migrations)
            await _orchestrator.EnqueueMigrationAsync(mid, cancellationToken);

        if (migrations.Count > 0)
            _log.LogInformation("Crash-resume re-enqueued {Count} interrupted migrations.", migrations.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~CrashResumeStartupServiceTests` → expected **PASS** (2 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/Startup src/EMaigrator.Workers.Tests/Startup/CrashResumeStartupServiceTests.cs
git commit -m "feat(workers): crash-resume startup service re-enqueues interrupted migrations

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: WorkerServiceRegistration — DI + MassTransit topology with DLQ retry from OrchestrationOptions

**Goal:** Implement the `IServiceCollection` extension `AddEmaigratorWorkers()` that registers all consumers, the control gate, session factory, copier factory, and the MassTransit/RabbitMQ topology with prefetch + a redelivery/retry policy that lands poison `MigrateBatch` messages in the DLQ after `OrchestrationOptions.DlqRetryCount` attempts.

**Files:**
- Create: `src/EMaigrator.Workers/WorkerServiceRegistration.cs`
- Test: `src/EMaigrator.Workers.Tests/WorkerServiceRegistrationTests.cs`

**Acceptance Criteria:**
- [ ] `AddEmaigratorWorkers(this IServiceCollection services, IConfiguration config)` registers: `IMigrationControlGate`→`RedisMigrationControlGate`, `IProviderSessionFactory`→`ProviderSessionFactory`, `StreamingCopierFactory`, `IJobOrchestrator`→`MassTransitJobOrchestrator`, and all consumers (`StartMigrationConsumer`, `MigrateFolderConsumer`, `MigrateBatchConsumer`, `MigrateBatchFaultConsumer`, `JobControlConsumer`) plus `CrashResumeStartupService` as a hosted service.
- [ ] `OrchestrationOptions` is bound from configuration section `"Orchestration"`.
- [ ] MassTransit is added via `AddMassTransit` with the in-memory test transport selectable so the registration is verifiable without RabbitMQ; production path uses RabbitMQ with `PrefetchCount = OrchestrationOptions.ConsumerPrefetch` and a message retry of `OrchestrationOptions.DlqRetryCount` immediate retries before fault/DLQ.
- [ ] A unit test builds the provider and resolves `IJobOrchestrator`, `IMigrationControlGate`, and the registered `IConsumer` types successfully, and asserts `OrchestrationOptions.DlqRetryCount` is bound from config.

**Verify:** `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~WorkerServiceRegistrationTests` → all pass.

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.Tests/WorkerServiceRegistrationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Workers;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EMaigrator.Workers.Tests;

public sealed class WorkerServiceRegistrationTests
{
    private static ServiceProvider Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:BatchSize"] = "100",
                ["Orchestration:DlqRetryCount"] = "5",
                ["Orchestration:ConsumerPrefetch"] = "16",
                ["Workers:UseInMemoryTransport"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // External seams supplied by Infrastructure/connectors at runtime — substituted here.
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<ISecretStore>());
        services.AddSingleton(Substitute.For<ILedger>());
        services.AddSingleton(Substitute.For<IRateLimiter>());
        services.AddSingleton(Substitute.For<IMigrationConnectionLookup>());
        services.AddSingleton(Substitute.For<IMessageRefLister>());
        services.AddSingleton(Substitute.For<IMessageHydrator>());
        services.AddSingleton(Substitute.For<IRemediationPlanStore>());
        services.AddSingleton(Substitute.For<IJobMigrationLookup>());
        services.AddSingleton(Substitute.For<IInterruptedJobLookup>());

        services.AddEmaigratorWorkers(config);
        return services.BuildServiceProvider(true);
    }

    [Fact]
    public void Registers_core_worker_services()
    {
        using var provider = Build();
        provider.GetService<IJobOrchestrator>().Should().BeOfType<MassTransitJobOrchestrator>();
        provider.GetService<IMigrationControlGate>().Should().BeOfType<RedisMigrationControlGate>();
        provider.GetService<IProviderSessionFactory>().Should().BeOfType<ProviderSessionFactory>();
        provider.GetService<EMaigrator.Workers.Copy.StreamingCopierFactory>().Should().NotBeNull();
    }

    [Fact]
    public void Binds_orchestration_options_from_config()
    {
        using var provider = Build();
        var opts = provider.GetRequiredService<IOptions<OrchestrationOptions>>().Value;
        opts.DlqRetryCount.Should().Be(5);
        opts.BatchSize.Should().Be(100);
        opts.ConsumerPrefetch.Should().Be(16);
    }

    [Fact]
    public void Registers_crash_resume_hosted_service()
    {
        using var provider = Build();
        var hosted = provider.GetServices<IHostedService>();
        hosted.Should().Contain(h => h is CrashResumeStartupService);
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~WorkerServiceRegistrationTests` → expected **FAIL**: `AddEmaigratorWorkers` does not exist (CS0246/CS1061).

3. - [ ] Implement `src/EMaigrator.Workers/WorkerServiceRegistration.cs`:

```csharp
using System;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Workers;

public static class WorkerServiceRegistration
{
    public static IServiceCollection AddEmaigratorWorkers(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<OrchestrationOptions>(config.GetSection("Orchestration"));
        var orchestration = config.GetSection("Orchestration").Get<OrchestrationOptions>() ?? new OrchestrationOptions();
        var useInMemory = config.GetValue("Workers:UseInMemoryTransport", false);

        services.AddSingleton<IMigrationControlGate, RedisMigrationControlGate>();
        services.AddSingleton<IProviderSessionFactory, ProviderSessionFactory>();
        services.AddSingleton<StreamingCopierFactory>();
        services.AddScoped<IJobOrchestrator, MassTransitJobOrchestrator>();
        services.AddHostedService<CrashResumeStartupService>();

        services.AddMassTransit(x =>
        {
            x.AddConsumer<StartMigrationConsumer>();
            x.AddConsumer<MigrateFolderConsumer>();
            x.AddConsumer<MigrateBatchConsumer>();
            x.AddConsumer<MigrateBatchFaultConsumer>();
            x.AddConsumer<JobControlConsumer>();

            if (useInMemory)
            {
                x.UsingInMemory((ctx, cfg) =>
                {
                    cfg.PrefetchCount = orchestration.ConsumerPrefetch;
                    cfg.UseMessageRetry(r => r.Immediate(orchestration.DlqRetryCount));
                    cfg.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    var host = config.GetConnectionString("RabbitMq") ?? "amqp://guest:guest@localhost:5672";
                    cfg.Host(new Uri(host));
                    cfg.PrefetchCount = orchestration.ConsumerPrefetch;
                    // Immediate retries, then the message is faulted → DLQ → MigrateBatchFaultConsumer.
                    cfg.UseMessageRetry(r => r.Immediate(orchestration.DlqRetryCount));
                    cfg.ConfigureEndpoints(ctx);
                });
            }
        });

        return services;
    }
}
```

4. - [ ] Run `dotnet test src/EMaigrator.Workers.Tests --filter FullyQualifiedName~WorkerServiceRegistrationTests` → expected **PASS** (3 tests green).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers/WorkerServiceRegistration.cs src/EMaigrator.Workers.Tests/WorkerServiceRegistrationTests.cs
git commit -m "feat(workers): DI + MassTransit topology with prefetch and DLQ retry policy

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: Functional Verification — full E2E pipeline with Testcontainers (copy, idempotency, crash-resume, DLQ)

**Goal:** Prove the subsystem's headline behavior end-to-end with real infrastructure: a GreenMail IMAP source + IMAP destination, Postgres ledger, RabbitMQ queue, Redis rate-limiter/control, asserting copy correctness, idempotent re-run (zero dupes), crash-resume completion, and DLQ on a poison message.

**Files:**
- Create: `src/EMaigrator.Workers.IntegrationTests/EmaigratorPipelineFixture.cs`
- Create: `src/EMaigrator.Workers.IntegrationTests/EndToEndPipelineTests.cs`
- Test: `src/EMaigrator.Workers.IntegrationTests/EndToEndPipelineTests.cs`

**Acceptance Criteria:**
- [ ] The fixture starts four Testcontainers: `Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `Testcontainers.Redis`, and a GreenMail container (`greenmail/standalone:2.1.0`) exposing IMAP on 3143 and SMTP on 3025, used both as source and destination (different mailboxes).
- [ ] Seeds the source mailbox with 20 messages across 2 folders via SMTP/IMAP APPEND; runs the full pipeline through the real bus + real consumers + real IMAP connector (`EMaigrator.Connectors.Imap`).
- [ ] **Copy correctness:** destination mailbox contains all 20 messages with matching `Message-ID` headers after the run.
- [ ] **Idempotent re-run:** running the pipeline a second time leaves the destination at exactly 20 messages (zero duplicates); the ledger shows 20 `Migrated` rows, no growth.
- [ ] **Crash-resume:** kill the consuming host after ~half the messages copy, restart it, and the migration completes to 20 with zero dupes.
- [ ] **DLQ:** one deliberately-poisoned message (oversized beyond a constrained `MaxMessageBytes`) ends in a `NeedsDecisionEvent`; the other 19 still complete; the ledger marks the poison message `Failed`, not `Migrated`.

**Verify:** `dotnet test src/EMaigrator.Workers.IntegrationTests --filter FullyQualifiedName~EndToEndPipelineTests` → all pass (Docker required).

**Steps:**

1. - [ ] Write the failing test `src/EMaigrator.Workers.IntegrationTests/EndToEndPipelineTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Workers.IntegrationTests;

[Collection("pipeline")]
public sealed class EndToEndPipelineTests : IClassFixture<EmaigratorPipelineFixture>
{
    private readonly EmaigratorPipelineFixture _fx;
    public EndToEndPipelineTests(EmaigratorPipelineFixture fx) => _fx = fx;

    [Fact]
    public async Task Copies_all_messages_with_matching_message_ids()
    {
        var mid = await _fx.SeedSourceAsync(messageCount: 20, folders: new[] { "INBOX", "Archive" });
        await _fx.RunPipelineToCompletionAsync(mid, TimeSpan.FromMinutes(3));

        var srcIds = await _fx.GetDestMessageIdsAsync();
        srcIds.Should().HaveCount(20);
        (await _fx.LedgerCountAsync(mid, "Migrated")).Should().Be(20);
    }

    [Fact]
    public async Task Rerun_is_idempotent_zero_duplicates()
    {
        var mid = await _fx.SeedSourceAsync(messageCount: 20, folders: new[] { "INBOX", "Archive" });
        await _fx.RunPipelineToCompletionAsync(mid, TimeSpan.FromMinutes(3));
        await _fx.RunPipelineToCompletionAsync(mid, TimeSpan.FromMinutes(3)); // re-run

        (await _fx.GetDestMessageIdsAsync()).Should().HaveCount(20);
        (await _fx.LedgerCountAsync(mid, "Migrated")).Should().Be(20);
    }

    [Fact]
    public async Task Crash_midrun_then_restart_completes()
    {
        var mid = await _fx.SeedSourceAsync(messageCount: 20, folders: new[] { "INBOX", "Archive" });
        await _fx.RunPipelineThenKillAfterAsync(mid, killAfterMessages: 8);
        await _fx.RestartAndRunToCompletionAsync(mid, TimeSpan.FromMinutes(3));

        (await _fx.GetDestMessageIdsAsync()).Should().HaveCount(20);
        (await _fx.LedgerCountAsync(mid, "Migrated")).Should().Be(20);
    }

    [Fact]
    public async Task Poison_message_goes_to_dlq_others_complete()
    {
        var mid = await _fx.SeedSourceWithOnePoisonAsync(messageCount: 20, maxMessageBytes: 1024);
        var decisions = await _fx.RunPipelineCollectingDecisionsAsync(mid, TimeSpan.FromMinutes(3));

        decisions.Should().ContainSingle(d => d.IssueType == "PoisonBatch");
        (await _fx.LedgerCountAsync(mid, "Migrated")).Should().Be(19);
        (await _fx.LedgerCountAsync(mid, "Failed")).Should().BeGreaterThanOrEqualTo(1);
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.IntegrationTests --filter FullyQualifiedName~EndToEndPipelineTests` → expected **FAIL**: `EmaigratorPipelineFixture` and its methods do not exist (CS0246).

3. - [ ] Implement `src/EMaigrator.Workers.IntegrationTests/EmaigratorPipelineFixture.cs` (wires the four containers + the real IMAP connector + the worker DI, and exposes the helper methods used by the test):

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Contracts;
using EMaigrator.Workers;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Sessions;
using MailKit.Net.Imap;
using MailKit.Security;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace EMaigrator.Workers.IntegrationTests;

public sealed class EmaigratorPipelineFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RabbitMqContainer _mq = new RabbitMqBuilder().WithImage("rabbitmq:3.13-management").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    private readonly IContainer _greenmail = new ContainerBuilder()
        .WithImage("greenmail/standalone:2.1.0")
        .WithEnvironment("GREENMAIL_OPTS",
            "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.users=src:pw@example.com,dst:pw@example.com -Dgreenmail.auth.disabled")
        .WithPortBinding(3025, true)  // SMTP
        .WithPortBinding(3143, true)  // IMAP
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3143))
        .Build();

    private IHost? _host;
    private readonly ConcurrentBag<NeedsDecisionEvent> _decisions = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_pg.StartAsync(), _mq.StartAsync(), _redis.StartAsync(), _greenmail.StartAsync());
        await BuildHostAsync(killAfterMessages: null);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.StopAsync();
        await Task.WhenAll(_pg.DisposeAsync().AsTask(), _mq.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(), _greenmail.DisposeAsync().AsTask());
    }

    private int ImapPort => _greenmail.GetMappedPublicPort(3143);
    private int SmtpPort => _greenmail.GetMappedPublicPort(3025);

    private async Task BuildHostAsync(int? killAfterMessages)
    {
        // A NeedsDecisionEvent collector consumer feeds _decisions.
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RabbitMq"] = _mq.GetConnectionString(),
            ["ConnectionStrings:Postgres"] = _pg.GetConnectionString(),
            ["Orchestration:BatchSize"] = "5",
            ["Orchestration:DlqRetryCount"] = "2",
            ["Orchestration:ConsumerPrefetch"] = "8",
            ["Workers:UseInMemoryTransport"] = "false"
        });

        builder.Services.AddLogging();
        builder.Services.AddSingleton(StackExchange.Redis.ConnectionMultiplexer.Connect(_redis.GetConnectionString()));
        builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
            sp => sp.GetRequiredService<StackExchange.Redis.ConnectionMultiplexer>());

        // Real infra seams (ledger over Postgres, rate-limiter over Redis, secret store local-key)
        // are registered by Infrastructure's AddEmaigratorInfrastructure (Plan 03).
        builder.Services.AddEmaigratorInfrastructure(builder.Configuration);
        // Real IMAP connector (Plan 04) — used as both source and destination.
        builder.Services.AddImapConnector();
        // Test doubles for lookups that, in production, read EF entities (Plan 03/08 wire these).
        builder.Services.AddSingleton<IMigrationConnectionLookup>(new TestConnectionLookup(ImapPort));
        builder.Services.AddSingleton<IMessageRefLister, ImapMessageRefLister>();
        builder.Services.AddSingleton<IMessageHydrator, ImapMessageHydrator>();
        builder.Services.AddSingleton<EMaigrator.Workers.Remediation.IRemediationPlanStore, EmptyRemediationStore>();
        builder.Services.AddSingleton<IJobMigrationLookup, LedgerJobMigrationLookup>();
        builder.Services.AddSingleton<EMaigrator.Workers.Startup.IInterruptedJobLookup, LedgerInterruptedJobLookup>();

        builder.Services.AddEmaigratorWorkers(builder.Configuration);
        // Collector consumer registered onto the same bus to capture decisions for assertions.
        builder.Services.AddSingleton(_decisions);

        _host = builder.Build();
        await _host.StartAsync();
    }

    // ---- Helpers used by the tests ----

    public Task<Guid> SeedSourceAsync(int messageCount, string[] folders) =>
        SeedInternalAsync(messageCount, folders, poison: false, maxMessageBytes: long.MaxValue);

    public Task<Guid> SeedSourceWithOnePoisonAsync(int messageCount, long maxMessageBytes) =>
        SeedInternalAsync(messageCount, new[] { "INBOX" }, poison: true, maxMessageBytes);

    private async Task<Guid> SeedInternalAsync(int messageCount, string[] folders, bool poison, long maxMessageBytes)
    {
        var mid = await CreateMigrationAsync(maxMessageBytes);
        using var smtp = new SmtpClient("127.0.0.1", SmtpPort) { Credentials = new NetworkCredential("src", "pw") };
        for (var i = 0; i < messageCount; i++)
        {
            var body = (poison && i == messageCount - 1) ? new string('X', 4096) : $"body {i}";
            var msg = new MailMessage("from@example.com", "src@example.com", $"Subject {i}", body)
            {
                Headers = { { "Message-ID", $"<msg-{i}-{mid:N}@example.com>" } }
            };
            var folder = folders[i % folders.Length];
            await AppendToImapAsync("src", folder, msg);
        }
        return mid;
    }

    public async Task RunPipelineToCompletionAsync(Guid mid, TimeSpan timeout)
    {
        var orch = _host!.Services.GetRequiredService<IJobOrchestrator>();
        await orch.EnqueueMigrationAsync(mid, CancellationToken.None);
        await WaitUntilLedgerSettledAsync(mid, timeout);
    }

    public async Task RunPipelineThenKillAfterAsync(Guid mid, int killAfterMessages)
    {
        var orch = _host!.Services.GetRequiredService<IJobOrchestrator>();
        await orch.EnqueueMigrationAsync(mid, CancellationToken.None);
        await WaitUntilLedgerCountAtLeastAsync(mid, killAfterMessages, TimeSpan.FromMinutes(2));
        await _host.StopAsync();           // simulate crash — in-flight batches un-acked, redeliver
        _host.Dispose();
        _host = null;
    }

    public async Task RestartAndRunToCompletionAsync(Guid mid, TimeSpan timeout)
    {
        await BuildHostAsync(killAfterMessages: null);  // CrashResumeStartupService re-enqueues not-done
        await WaitUntilLedgerSettledAsync(mid, timeout);
    }

    public async Task<IReadOnlyList<NeedsDecisionEvent>> RunPipelineCollectingDecisionsAsync(Guid mid, TimeSpan timeout)
    {
        _decisions.Clear();
        await RunPipelineToCompletionAsync(mid, timeout);
        return _decisions.ToList();
    }

    public async Task<IReadOnlyList<string>> GetDestMessageIdsAsync()
    {
        var ids = new List<string>();
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync("dst", "pw");
        foreach (var folderName in new[] { "INBOX", "Archive" })
        {
            try
            {
                var folder = await client.GetFolderAsync(folderName);
                await folder.OpenAsync(MailKit.FolderAccess.ReadOnly);
                for (var i = 0; i < folder.Count; i++)
                {
                    var m = await folder.GetMessageAsync(i);
                    if (!string.IsNullOrEmpty(m.MessageId)) ids.Add(m.MessageId);
                }
            }
            catch (MailKit.FolderNotFoundException) { /* folder may not exist on dest */ }
        }
        await client.DisconnectAsync(true);
        return ids;
    }

    public async Task<long> LedgerCountAsync(Guid mid, string status)
    {
        var ledger = _host!.Services.GetRequiredService<ILedger>();
        var counts = await ledger.GetCountsAsync(mid, CancellationToken.None);
        return status switch
        {
            "Migrated" => counts.Migrated,
            "Skipped" => counts.Skipped,
            "Failed" => counts.Failed,
            "Pending" => counts.Pending,
            _ => 0
        };
    }

    // Implementations of CreateMigrationAsync, AppendToImapAsync, WaitUntilLedgerSettledAsync,
    // WaitUntilLedgerCountAtLeastAsync, TestConnectionLookup, ImapMessageRefLister,
    // ImapMessageHydrator, EmptyRemediationStore, LedgerJobMigrationLookup,
    // LedgerInterruptedJobLookup are provided in PipelineSupport.cs (added in this task).
    private async Task<Guid> CreateMigrationAsync(long maxMessageBytes) => await PipelineSupport.CreateMigrationAsync(_host!, ImapPort, maxMessageBytes);
    private Task AppendToImapAsync(string user, string folder, MailMessage msg) => PipelineSupport.AppendAsync("127.0.0.1", ImapPort, user, folder, msg);
    private Task WaitUntilLedgerSettledAsync(Guid mid, TimeSpan timeout) => PipelineSupport.WaitSettledAsync(_host!, mid, timeout);
    private Task WaitUntilLedgerCountAtLeastAsync(Guid mid, int n, TimeSpan timeout) => PipelineSupport.WaitCountAsync(_host!, mid, n, timeout);
}
```

   Add `src/EMaigrator.Workers.IntegrationTests/PipelineSupport.cs` implementing the helpers referenced above:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;

namespace EMaigrator.Workers.IntegrationTests;

internal static class PipelineSupport
{
    public static async Task<Guid> CreateMigrationAsync(IHost host, int imapPort, long maxMessageBytes)
    {
        // Persist a MailboxMigration + its source/dest connections through Infrastructure's
        // provisioning helper (Plan 03 exposes a test seam ISeedStore). Returns the new id.
        var seed = host.Services.GetRequiredService<EMaigrator.Infrastructure.Testing.ISeedStore>();
        return await seed.CreateImapToImapMigrationAsync(imapPort, "src", "dst", "pw", maxMessageBytes);
    }

    public static async Task AppendAsync(string host, int port, string user, string folderName, MailMessage net)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(net.From!.Address));
        mime.To.Add(MailboxAddress.Parse(net.To[0].Address));
        mime.Subject = net.Subject;
        mime.MessageId = net.Headers["Message-ID"];
        mime.Body = new TextPart("plain") { Text = net.Body };

        using var client = new ImapClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.None);
        await client.AuthenticateAsync(user, "pw");
        IMailFolder folder;
        if (folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            folder = client.Inbox;
        else
        {
            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            try { folder = await client.GetFolderAsync(folderName); }
            catch (FolderNotFoundException) { folder = await personal.CreateAsync(folderName, true); }
        }
        await folder.OpenAsync(FolderAccess.ReadWrite);
        await folder.AppendAsync(mime);
        await client.DisconnectAsync(true);
    }

    public static async Task WaitSettledAsync(IHost host, Guid mid, TimeSpan timeout)
    {
        var ledger = host.Services.GetRequiredService<ILedger>();
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var c = await ledger.GetCountsAsync(mid, CancellationToken.None);
            if (c.Pending == 0 && (c.Migrated + c.Skipped + c.Failed) > 0) return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"Migration {mid} did not settle within {timeout}.");
    }

    public static async Task WaitCountAsync(IHost host, Guid mid, int n, TimeSpan timeout)
    {
        var ledger = host.Services.GetRequiredService<ILedger>();
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var c = await ledger.GetCountsAsync(mid, CancellationToken.None);
            if (c.Migrated >= n) return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Migration {mid} did not reach {n} migrated within {timeout}.");
    }
}

internal sealed class TestConnectionLookup : IMigrationConnectionLookup
{
    private readonly int _imapPort;
    public TestConnectionLookup(int imapPort) => _imapPort = imapPort;
    public Task<MigrationConnections> GetAsync(Guid mid, CancellationToken ct)
    {
        var source = new ConnectionDescriptor
        {
            Provider = new("imap"), Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = _imapPort.ToString(), ["accountEmail"] = "src", ["tls"] = "false" },
            SecretRef = "src-secret"
        };
        var dest = new ConnectionDescriptor
        {
            Provider = new("imap"), Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = _imapPort.ToString(), ["accountEmail"] = "dst", ["tls"] = "false" },
            SecretRef = "dst-secret"
        };
        return Task.FromResult(new MigrationConnections(Guid.NewGuid(), "t1", source, dest));
    }
}

internal sealed class ImapMessageRefLister : IMessageRefLister
{
    public async IAsyncEnumerable<string> ListRefsAsync(ISourceProvider source, FolderPath folder,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var m in source.ReadMessagesAsync(folder, new ReadOptions(), ct))
            yield return m.IdentityKey; // ref == identity key for the IMAP source in this fixture
    }
}

internal sealed class ImapMessageHydrator : IMessageHydrator
{
    public async Task<CanonicalMessage> HydrateAsync(ISourceProvider source, FolderPath folder, string reference, CancellationToken ct)
    {
        await foreach (var m in source.ReadMessagesAsync(folder, new ReadOptions(), ct))
            if (m.IdentityKey == reference) return m;
        throw new InvalidOperationException($"Ref {reference} not found in {folder}.");
    }
}

internal sealed class EmptyRemediationStore : IRemediationPlanStore
{
    public Task<IReadOnlyList<ApprovedRemediation>> GetApprovedAsync(Guid mid, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ApprovedRemediation>>(Array.Empty<ApprovedRemediation>());
}

internal sealed class LedgerJobMigrationLookup : IJobMigrationLookup
{
    public Task<IReadOnlyList<Guid>> GetNotDoneMigrationsAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
}

internal sealed class LedgerInterruptedJobLookup : IInterruptedJobLookup
{
    private readonly EMaigrator.Infrastructure.Testing.ISeedStore _seed;
    public LedgerInterruptedJobLookup(EMaigrator.Infrastructure.Testing.ISeedStore seed) => _seed = seed;
    public Task<IReadOnlyList<Guid>> GetRunningMigrationsToResumeAsync(CancellationToken ct)
        => _seed.GetRunningMigrationIdsAsync(ct);
}
```

> The fixture binds to seams that Plans 03 (`AddEmaigratorInfrastructure`, `EMaigrator.Infrastructure.Testing.ISeedStore`) and 04 (`AddImapConnector`) own. This plan does not re-implement them; it consumes them per CONTRACTS. If those seams are not yet merged, this integration test is `[Trait("requires","infra+imap")]` and gated in CI behind their availability — the unit tasks (1–11) remain independently green.

   Add the trait/skip guard at the top of `EndToEndPipelineTests` so the suite is honest when prerequisites are absent: annotate the class `[Trait("Category", "E2E")]` and run only when `DOCKER_AVAILABLE=1`.

4. - [ ] Run `dotnet test src/EMaigrator.Workers.IntegrationTests --filter FullyQualifiedName~EndToEndPipelineTests` → expected **PASS** (4 tests green; Docker + Infra/IMAP seams present).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers.IntegrationTests
git commit -m "test(workers): E2E Testcontainers pipeline — copy, idempotency, crash-resume, DLQ

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: Security Verification — zero body bytes persisted, content-free DLQ, rate-limiter prevents lockout

**Goal:** Prove the workers' security focus from the INDEX per-plan table: a real streaming migration persists zero message-body/attachment bytes to Postgres or disk, DLQ payloads carry no message content, and the rate limiter prevents exceeding the configured provider limit under concurrency.

**USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Workers.IntegrationTests/Security/NoBodyPersistedTests.cs`
- Create: `src/EMaigrator.Workers.IntegrationTests/Security/TempDirWatcher.cs`
- Create: `src/EMaigrator.Workers.IntegrationTests/Security/DlqContentFreeTests.cs`
- Create: `src/EMaigrator.Workers.IntegrationTests/Security/RateLimiterLockoutTests.cs`
- Test: all three files above.

**Acceptance Criteria:**
- [ ] **No body in Postgres:** after a real streaming migration (reusing `EmaigratorPipelineFixture`) of messages whose bodies contain a unique sentinel string `EMAIGRATOR_BODY_SENTINEL_{guid}`, a raw SQL scan of every text/jsonb/bytea column of every table in the Postgres container finds **zero** rows containing the sentinel. Captured output: the executed query and a `0` row count per table.
- [ ] **No body on disk:** a `TempDirWatcher` snapshots the process temp dir + working dir before the run and re-scans after; no new file contains the sentinel bytes. Captured output: list of new files (expected empty) and a grep result of `0`.
- [ ] **DLQ content-free:** force a poison oversized message whose body holds the sentinel; assert the resulting `NeedsDecisionEvent.Detail` and `IssueType` contain neither the sentinel nor the subject text — only identity refs + folder + error type. Captured output: the serialized `NeedsDecisionEvent` JSON with no sentinel match.
- [ ] **Ledger/log columns hold no body:** assert `LedgerEntryRow` and `MigrationLogRow` (Plan 03 entities) have no column whose value equals or contains the body sentinel after the run (reuses the SQL scan, filtered to those two tables).
- [ ] **Rate limiter prevents lockout:** with a real Redis `IRateLimiter` configured to `BucketSpec{ RefillPerSecond = 5, Burst = 5 }`, fire 200 concurrent `TryAcquireAsync` calls for one `(provider, account)` over a 1-second window; assert the number of *granted* tokens ≤ `Burst + ceil(RefillPerSecond * elapsedSeconds)` (i.e. never exceeds the configured provider limit), proving uncoordinated workers cannot blow the limit. Captured output: granted-count vs computed ceiling.

**Verify:** `dotnet test src/EMaigrator.Workers.IntegrationTests --filter FullyQualifiedName~Security` → all pass (Docker required).

**Steps:**

1. - [ ] Write the failing tests. `src/EMaigrator.Workers.IntegrationTests/Security/TempDirWatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EMaigrator.Workers.IntegrationTests.Security;

/// <summary>Snapshots temp + working dirs before a run; finds new files that contain a sentinel after.</summary>
public sealed class TempDirWatcher
{
    private readonly string[] _roots;
    private HashSet<string> _before = new();

    public TempDirWatcher()
    {
        _roots = new[] { Path.GetTempPath(), Directory.GetCurrentDirectory() };
    }

    public void Snapshot() => _before = Enumerate().ToHashSet();

    public IReadOnlyList<string> NewFilesContaining(string sentinel)
    {
        var hits = new List<string>();
        foreach (var f in Enumerate())
        {
            if (_before.Contains(f)) continue;
            try
            {
                var bytes = File.ReadAllText(f);
                if (bytes.Contains(sentinel)) hits.Add(f);
            }
            catch { /* locked / binary — ignore for the sentinel text scan */ }
        }
        return hits;
    }

    private IEnumerable<string> Enumerate()
    {
        foreach (var root in _roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var f in files) yield return f;
        }
    }
}
```

   `src/EMaigrator.Workers.IntegrationTests/Security/NoBodyPersistedTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Security;

[Trait("Category", "Security")]
[Collection("pipeline")]
public sealed class NoBodyPersistedTests : IClassFixture<EmaigratorPipelineFixture>
{
    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;
    public NoBodyPersistedTests(EmaigratorPipelineFixture fx, ITestOutputHelper @out) { _fx = fx; _out = @out; }

    [Fact]
    public async Task No_message_body_reaches_postgres_or_disk()
    {
        var sentinel = $"EMAIGRATOR_BODY_SENTINEL_{Guid.NewGuid():N}";
        var watcher = new TempDirWatcher();
        watcher.Snapshot();

        var mid = await _fx.SeedSourceWithBodySentinelAsync(messageCount: 10, sentinel);
        await _fx.RunPipelineToCompletionAsync(mid, TimeSpan.FromMinutes(3));

        // 1) Disk: no new temp/working file contains the sentinel.
        var diskHits = watcher.NewFilesContaining(sentinel);
        _out.WriteLine($"New files containing sentinel: {diskHits.Count}");
        diskHits.Should().BeEmpty();

        // 2) Postgres: scan every text/varchar/jsonb/bytea column of every table for the sentinel.
        var hits = await ScanPostgresForSentinelAsync(_fx.PostgresConnectionString, sentinel);
        foreach (var (table, column, count) in hits)
            _out.WriteLine($"{table}.{column} sentinel matches: {count}");
        hits.Should().OnlyContain(h => h.Count == 0);
    }

    private static async Task<IReadOnlyList<(string Table, string Column, long Count)>> ScanPostgresForSentinelAsync(
        string connString, string sentinel)
    {
        var results = new List<(string, string, long)>();
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var columns = new List<(string Table, string Column)>();
        await using (var cmd = new NpgsqlCommand(
            @"SELECT table_name, column_name FROM information_schema.columns
              WHERE table_schema='public'
                AND data_type IN ('text','character varying','jsonb','json','bytea','character')", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) columns.Add((r.GetString(0), r.GetString(1)));

        foreach (var (table, column) in columns)
        {
            var sql = $"SELECT COUNT(*) FROM \"{table}\" WHERE CAST(\"{column}\" AS text) LIKE @p";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p", $"%{sentinel}%");
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            results.Add((table, column, count));
        }
        return results;
    }
}
```

   `src/EMaigrator.Workers.IntegrationTests/Security/DlqContentFreeTests.cs`:

```csharp
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Security;

[Trait("Category", "Security")]
[Collection("pipeline")]
public sealed class DlqContentFreeTests : IClassFixture<EmaigratorPipelineFixture>
{
    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;
    public DlqContentFreeTests(EmaigratorPipelineFixture fx, ITestOutputHelper @out) { _fx = fx; _out = @out; }

    [Fact]
    public async Task Dlq_needs_decision_carries_no_body_or_subject()
    {
        var sentinel = $"EMAIGRATOR_BODY_SENTINEL_{Guid.NewGuid():N}";
        var subject = $"SUBJECT_SENTINEL_{Guid.NewGuid():N}";
        var mid = await _fx.SeedSourceWithOnePoisonSentinelAsync(messageCount: 10, maxMessageBytes: 1024, sentinel, subject);

        var decisions = await _fx.RunPipelineCollectingDecisionsAsync(mid, TimeSpan.FromMinutes(3));
        var poison = decisions.Single(d => d.IssueType == "PoisonBatch");
        var json = JsonSerializer.Serialize(poison);
        _out.WriteLine($"NeedsDecisionEvent: {json}");

        json.Should().NotContain(sentinel);     // no body content
        json.Should().NotContain(subject);      // no subject content
        poison.Detail.Should().Contain("folder=").And.Contain("errorType=").And.Contain("refs=");
    }
}
```

   `src/EMaigrator.Workers.IntegrationTests/Security/RateLimiterLockoutTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Security;

[Trait("Category", "Security")]
[Collection("pipeline")]
public sealed class RateLimiterLockoutTests : IClassFixture<EmaigratorPipelineFixture>
{
    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;
    public RateLimiterLockoutTests(EmaigratorPipelineFixture fx, ITestOutputHelper @out) { _fx = fx; _out = @out; }

    [Fact]
    public async Task Concurrent_acquires_never_exceed_configured_limit()
    {
        // Real Redis-backed IRateLimiter from Infrastructure (Plan 03), configured 5/s burst 5.
        var limiter = _fx.CreateRateLimiter(new BucketSpec { RefillPerSecond = 5, Burst = 5 });
        var key = new RateLimitKey(new ProviderId("graph"), "dest@biz.com");

        var start = DateTimeOffset.UtcNow;
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => limiter.TryAcquireAsync(key, 1, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        var elapsed = (DateTimeOffset.UtcNow - start).TotalSeconds;

        var granted = results.Count(r => r);
        var ceiling = 5 /* burst */ + (int)Math.Ceiling(5 /* refill/s */ * elapsed) + 1 /* tolerance */;
        _out.WriteLine($"granted={granted} ceiling={ceiling} elapsed={elapsed:F3}s");

        granted.Should().BeLessThanOrEqualTo(ceiling,
            "uncoordinated workers must not collectively exceed the provider's configured limit");
        granted.Should().BeGreaterThan(0, "the bucket must allow at least its burst");
    }
}
```

2. - [ ] Run `dotnet test src/EMaigrator.Workers.IntegrationTests --filter FullyQualifiedName~Security` → expected **FAIL**: the new fixture seams (`SeedSourceWithBodySentinelAsync`, `SeedSourceWithOnePoisonSentinelAsync`, `PostgresConnectionString`, `CreateRateLimiter`) do not exist (CS1061).

3. - [ ] Implement the fixture additions in `src/EMaigrator.Workers.IntegrationTests/EmaigratorPipelineFixture.cs`:

```csharp
    public string PostgresConnectionString => _pg.GetConnectionString();

    public Task<Guid> SeedSourceWithBodySentinelAsync(int messageCount, string sentinel) =>
        SeedSentinelInternalAsync(messageCount, sentinel, subject: $"plain-{Guid.NewGuid():N}", poison: false, maxMessageBytes: long.MaxValue);

    public Task<Guid> SeedSourceWithOnePoisonSentinelAsync(int messageCount, long maxMessageBytes, string sentinel, string subject) =>
        SeedSentinelInternalAsync(messageCount, sentinel, subject, poison: true, maxMessageBytes);

    private async Task<Guid> SeedSentinelInternalAsync(int messageCount, string sentinel, string subject, bool poison, long maxMessageBytes)
    {
        var mid = await CreateMigrationAsync(maxMessageBytes);
        for (var i = 0; i < messageCount; i++)
        {
            var oversize = poison && i == messageCount - 1;
            var body = oversize
                ? sentinel + new string('X', 4096)
                : $"{sentinel} body {i}";
            var msg = new System.Net.Mail.MailMessage("from@example.com", "src@example.com",
                oversize ? subject : $"Subject {i}", body)
            {
                Headers = { { "Message-ID", $"<msg-{i}-{mid:N}@example.com>" } }
            };
            await AppendToImapAsync("src", "INBOX", msg);
        }
        return mid;
    }

    public IRateLimiter CreateRateLimiter(BucketSpec spec)
    {
        // Infrastructure exposes a factory to build a Redis token-bucket limiter for a given spec (Plan 03).
        var factory = _host!.Services.GetRequiredService<EMaigrator.Infrastructure.Testing.IRateLimiterFactory>();
        return factory.Create("graph:dest@biz.com", spec);
    }
```

   (Add the necessary `using EMaigrator.Core.Configuration;`, `using EMaigrator.Core.Abstractions;`, and `using Microsoft.Extensions.DependencyInjection;` to the fixture file if not already present.)

4. - [ ] Run `dotnet test src/EMaigrator.Workers.IntegrationTests --filter FullyQualifiedName~Security` → expected **PASS** (3 security tests green; sentinel found in **zero** Postgres columns and **zero** disk files; DLQ JSON free of sentinel + subject; granted tokens ≤ ceiling).

5. - [ ] Commit:

```
git add src/EMaigrator.Workers.IntegrationTests/Security src/EMaigrator.Workers.IntegrationTests/EmaigratorPipelineFixture.cs
git commit -m "test(workers): security gate — zero body persistence, content-free DLQ, rate-limit ceiling

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Definition of Done (this plan)

- Tasks 1–11 are independently unit-green via the MassTransit in-memory harness + NSubstitute fakes (no Docker required).
- Task 12 proves the headline behavior end-to-end (copy correctness, idempotent zero-dupe re-run, crash-resume, DLQ) against real Postgres + RabbitMQ + Redis + GreenMail IMAP.
- Task 13 (USER-ORDERED GATE) proves zero body persistence (Postgres + disk), content-free DLQ payloads, and that the rate limiter prevents exceeding the configured provider limit under concurrency.
- All consumers bind to the frozen `EMaigrator.Core.Contracts` messages and `EMaigrator.Core.Abstractions` seams verbatim; `EMaigrator.Workers` references only `Core` + `Infrastructure` (dependency rule honored).
