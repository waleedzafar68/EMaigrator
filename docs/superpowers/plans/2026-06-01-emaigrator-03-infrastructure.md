# EMaigrator.Infrastructure Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Implement every Core abstraction that touches I/O — EF Core/PostgreSQL persistence (entities + migrations + ledger), `ISecretStore` (local-key AES-GCM envelope + KMS/Azure Key Vault seam), the Redis distributed token-bucket `IRateLimiter` with AIMD adaptive backoff, MassTransit/RabbitMQ `IJobOrchestrator` with DLQ, OpenTelemetry + Serilog wiring, ASP.NET health checks, and the 30-day log-purge + credential-purge-on-terminal background jobs — all behind the frozen CONTRACTS.md interfaces, composed via a single `AddInfrastructure` DI extension.

**Architecture:** `EMaigrator.Infrastructure` depends **only** on `EMaigrator.Core` abstractions (DESIGN.md §15); it owns concrete adapters and references no other connector/worker/api assembly. Persistence is PostgreSQL via EF Core with `LedgerEntryRow` carrying a `UNIQUE(MailboxMigrationId, IdentityKey)` constraint that makes `MarkAsync` an idempotent upsert; secrets are stored as ciphertext (envelope encryption) so a DB breach yields only ciphertext; Redis Lua scripts make token acquisition and 429 penalties atomic across horizontally-scaled workers. All adapters are verified with Testcontainers (Postgres, Redis, RabbitMQ) rather than mocks, because the contract is the database/broker behavior itself.

**Tech Stack:** C#/.NET 10 (LTS), EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL`, `MassTransit` + `MassTransit.RabbitMQ`, `StackExchange.Redis`, `Azure.Security.KeyVault.Secrets` + `Azure.Identity`, `Serilog.AspNetCore` + `Serilog.Sinks.OpenTelemetry`, `OpenTelemetry.Extensions.Hosting` + instrumentation packages, `AspNetCore.HealthChecks.NpgSql` / `.Redis` / `.Rabbitmq`. Tests: xUnit, FluentAssertions, NSubstitute, `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers.RabbitMq`.

---

### Task 1: Infrastructure project packages, DI extension skeleton, and InfrastructureOptions

**Goal:** Add the EF/Redis/MassTransit/OTel/KeyVault NuGet packages to `EMaigrator.Infrastructure` and create the empty `AddInfrastructure(IServiceCollection, IConfiguration)` DI entry point plus the `InfrastructureOptions` binding class so all later tasks compose into one seam.

**Files:**
- Modify: `src/EMaigrator.Infrastructure/EMaigrator.Infrastructure.csproj`
- Create: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure/InfrastructureOptions.cs`
- Modify: `src/EMaigrator.Infrastructure.Tests/EMaigrator.Infrastructure.Tests.csproj`
- Create: `src/EMaigrator.Infrastructure.Tests/DependencyInjectionTests.cs`

**Acceptance Criteria:**
- [ ] `EMaigrator.Infrastructure.csproj` references `EMaigrator.Core` (ProjectReference) and the listed NuGet packages only; no reference to any Connectors/Workers/Api/Cli project.
- [ ] `AddInfrastructure(this IServiceCollection services, IConfiguration config)` exists, binds `InfrastructureOptions` from the `"Infrastructure"` config section, and returns the `IServiceCollection`.
- [ ] `InfrastructureOptions` exposes `PostgresConnectionString`, `RedisConnectionString`, `RabbitMqConnectionString`, plus nested `SecretStoreOptions`, `RetentionOptions`, `OrchestrationOptions`, `RateLimitOptions` (the CONTRACTS.md §7 option types).
- [ ] A unit test builds a `ServiceCollection`, calls `AddInfrastructure` with an in-memory config, and resolves `IOptions<InfrastructureOptions>` with the bound values.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~DependencyInjectionTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/DependencyInjectionTests.cs`:

```csharp
using EMaigrator.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EMaigrator.Infrastructure.Tests;

public class DependencyInjectionTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:PostgresConnectionString"] = "Host=localhost;Database=emaigrator;Username=u;Password=p",
                ["Infrastructure:RedisConnectionString"] = "localhost:6379",
                ["Infrastructure:RabbitMqConnectionString"] = "amqp://guest:guest@localhost:5672",
                ["Infrastructure:SecretStore:Mode"] = "LocalKey",
                ["Infrastructure:SecretStore:KeyRef"] = "dGVzdC1rZXktMzItYnl0ZXMtYWVzLWdjbS1rZXkhIQ==",
                ["Infrastructure:Retention:LogRetentionDays"] = "30",
            })
            .Build();

    [Fact]
    public void AddInfrastructure_binds_options_from_config()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfig());
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;

        opts.PostgresConnectionString.Should().Contain("emaigrator");
        opts.RedisConnectionString.Should().Be("localhost:6379");
        opts.RabbitMqConnectionString.Should().StartWith("amqp://");
        opts.SecretStore.Mode.Should().Be("LocalKey");
        opts.Retention.LogRetentionDays.Should().Be(30);
    }

    [Fact]
    public void AddInfrastructure_returns_same_collection_for_chaining()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(BuildConfig()).Should().BeSameAs(services);
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~DependencyInjectionTests` → FAILS to compile: `AddInfrastructure` and `InfrastructureOptions` do not exist.

3. - [ ] **Minimal implementation.** Edit `src/EMaigrator.Infrastructure/EMaigrator.Infrastructure.csproj` to add the package references and the Core project reference:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>13</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Core\EMaigrator.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageReference Include="StackExchange.Redis" Version="2.8.16" />
    <PackageReference Include="MassTransit" Version="8.3.0" />
    <PackageReference Include="MassTransit.RabbitMQ" Version="8.3.0" />
    <PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.7.0" />
    <PackageReference Include="Azure.Identity" Version="1.13.1" />
    <PackageReference Include="Serilog.AspNetCore" Version="9.0.0" />
    <PackageReference Include="Serilog.Sinks.OpenTelemetry" Version="4.1.1" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.10.0" />
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
    <PackageReference Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />
    <PackageReference Include="AspNetCore.HealthChecks.Rabbitmq" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
  </ItemGroup>

</Project>
```

Create `src/EMaigrator.Infrastructure/InfrastructureOptions.cs`:

```csharp
using EMaigrator.Core.Configuration;

namespace EMaigrator.Infrastructure;

/// <summary>Root options for the Infrastructure subsystem; bound from the "Infrastructure" config section.</summary>
public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    public string PostgresConnectionString { get; set; } = "";
    public string RedisConnectionString { get; set; } = "";
    public string RabbitMqConnectionString { get; set; } = "";

    public SecretStoreOptions SecretStore { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();
    public OrchestrationOptions Orchestration { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
}
```

Create `src/EMaigrator.Infrastructure/DependencyInjection.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Composes the EMaigrator infrastructure adapters (persistence, secrets, rate limiter,
    /// orchestrator, observability, health checks, retention jobs) behind the Core abstractions.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<InfrastructureOptions>()
            .Bind(config.GetSection(InfrastructureOptions.SectionName))
            .ValidateOnStart();

        // Subsequent tasks register: DbContext, ILedger, ISecretStore, IRateLimiter,
        // IJobOrchestrator (MassTransit), OTel/Serilog, health checks, retention hosted services.
        return services;
    }
}
```

> Note: `OrchestrationOptions`, `RateLimitOptions`, `RetentionOptions`, `SecretStoreOptions` are defined in `EMaigrator.Core.Configuration` (CONTRACTS.md §7, delivered by Plan 02). This plan binds to them verbatim and never redefines them.

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~DependencyInjectionTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure src/EMaigrator.Infrastructure.Tests
git commit -m "feat(infra): add infrastructure packages and AddInfrastructure DI seam

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: EF Core entities and EmaigratorDbContext per CONTRACTS.md §5

**Goal:** Create the entity classes (`Job`, `MailboxMigration`, `FolderTask`, `LedgerEntryRow`, `MigrationLogRow`, `CredentialRow`, `Tenant`) exactly per CONTRACTS.md §5 and an `EmaigratorDbContext` whose model enforces `UNIQUE(MailboxMigrationId, IdentityKey)` on `LedgerEntryRow` and contains **no** body/attachment columns and no sender/recipient on `MigrationLogRow`.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Data/Job.cs`
- Create: `src/EMaigrator.Infrastructure/Data/MailboxMigration.cs`
- Create: `src/EMaigrator.Infrastructure/Data/FolderTask.cs`
- Create: `src/EMaigrator.Infrastructure/Data/LedgerEntryRow.cs`
- Create: `src/EMaigrator.Infrastructure/Data/MigrationLogRow.cs`
- Create: `src/EMaigrator.Infrastructure/Data/CredentialRow.cs`
- Create: `src/EMaigrator.Infrastructure/Data/Tenant.cs`
- Create: `src/EMaigrator.Infrastructure/Data/EmaigratorDbContext.cs`
- Create: `src/EMaigrator.Infrastructure/Data/ProviderIdValueConverter.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Data/DbContextModelTests.cs`

**Acceptance Criteria:**
- [ ] All seven entity types have exactly the fields listed in CONTRACTS.md §5 (no extra body/attachment/sender/recipient columns).
- [ ] `EmaigratorDbContext` exposes `DbSet<>` for each entity and maps `ProviderId` via a value converter to `text`.
- [ ] The model has a UNIQUE index on `(LedgerEntryRow.MailboxMigrationId, LedgerEntryRow.IdentityKey)`.
- [ ] A model-level test (no database) asserts: the unique index exists; `LedgerEntryRow` and `MigrationLogRow` expose no property whose name contains `body`/`attachment`/`content`; `MigrationLogRow` exposes no property containing `sender`/`recipient`/`from`/`to`.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~DbContextModelTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Data/DbContextModelTests.cs`:

```csharp
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Data;

public class DbContextModelTests
{
    private static EmaigratorDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<EmaigratorDbContext>()
            .UseNpgsql("Host=localhost;Database=design_only;Username=u;Password=p")
            .Options;
        return new EmaigratorDbContext(options);
    }

    [Fact]
    public void Ledger_has_unique_index_on_migration_and_identity_key()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(LedgerEntryRow))!;

        var unique = entity.GetIndexes().FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).OrderBy(n => n)
                .SequenceEqual(new[] { nameof(LedgerEntryRow.IdentityKey), nameof(LedgerEntryRow.MailboxMigrationId) }.OrderBy(n => n)));

        unique.Should().NotBeNull("ledger upsert idempotency relies on UNIQUE(MailboxMigrationId, IdentityKey)");
    }

    [Theory]
    [InlineData(typeof(LedgerEntryRow))]
    [InlineData(typeof(MigrationLogRow))]
    public void Metadata_tables_store_no_message_content(Type entityType)
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(entityType)!;
        var forbidden = new[] { "body", "attachment", "content", "payload", "raw", "mime" };

        var offending = entity.GetProperties()
            .Select(p => p.Name)
            .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        offending.Should().BeEmpty($"{entityType.Name} must never persist message content");
    }

    [Fact]
    public void MigrationLog_stores_no_sender_or_recipient()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(MigrationLogRow))!;
        var forbidden = new[] { "sender", "recipient", "from", "to", "cc", "bcc", "address" };

        var offending = entity.GetProperties()
            .Select(p => p.Name)
            .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        offending.Should().BeEmpty("MigrationLogRow must not record correspondents (DESIGN.md §10)");
    }

    [Fact]
    public void All_core_entities_are_mapped()
    {
        using var ctx = NewContext();
        foreach (var t in new[]
        {
            typeof(Job), typeof(MailboxMigration), typeof(FolderTask),
            typeof(LedgerEntryRow), typeof(MigrationLogRow), typeof(CredentialRow), typeof(Tenant)
        })
        {
            ctx.Model.FindEntityType(t).Should().NotBeNull($"{t.Name} must be mapped");
        }
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~DbContextModelTests` → FAILS to compile: entity types and `EmaigratorDbContext` do not exist.

3. - [ ] **Minimal implementation.** Create the entity files.

`src/EMaigrator.Infrastructure/Data/Job.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Infrastructure.Data;

public class Job
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public ProviderId SourceProvider { get; set; }
    public ProviderId DestProvider { get; set; }
    public string? SourceConnectionRef { get; set; }
    public string? DestConnectionRef { get; set; }
    public bool IsBatch { get; set; }
    public JobStatus Status { get; set; }
    public int WizardStep { get; set; }
    public bool StoreSubjects { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`src/EMaigrator.Infrastructure/Data/MailboxMigration.cs`:

```csharp
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Infrastructure.Data;

public class MailboxMigration
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string SourceMailbox { get; set; } = "";
    public string DestMailbox { get; set; } = "";
    public MailboxMigrationStatus Status { get; set; }
    public long MigratedCount { get; set; }
    public long SkippedCount { get; set; }
    public long FailedCount { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
```

`src/EMaigrator.Infrastructure/Data/FolderTask.cs`:

```csharp
namespace EMaigrator.Infrastructure.Data;

public class FolderTask
{
    public Guid Id { get; set; }
    public Guid MailboxMigrationId { get; set; }
    public string SourceFolder { get; set; } = "";
    public string DestFolder { get; set; } = "";
    public string Status { get; set; } = "";
}
```

`src/EMaigrator.Infrastructure/Data/LedgerEntryRow.cs`:

```csharp
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Infrastructure.Data;

/// <summary>
/// Idempotency ledger row. UNIQUE(MailboxMigrationId, IdentityKey).
/// NEVER stores message body, attachment, or subject. Identity hashes + folder mapping + status only.
/// </summary>
public class LedgerEntryRow
{
    public long Id { get; set; }
    public Guid MailboxMigrationId { get; set; }
    public string IdentityKey { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public string DestFolder { get; set; } = "";
    public LedgerStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`src/EMaigrator.Infrastructure/Data/MigrationLogRow.cs`:

```csharp
namespace EMaigrator.Infrastructure.Data;

/// <summary>
/// Migration audit log. Encrypted at rest; 30-day purge. Subject is nullable and omitted when
/// Job.StoreSubjects == false. NO sender/recipient.
/// </summary>
public class MigrationLogRow
{
    public long Id { get; set; }
    public Guid MailboxMigrationId { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset MessageDate { get; set; }
    public string SourceFolder { get; set; } = "";
    public string DestFolder { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

`src/EMaigrator.Infrastructure/Data/CredentialRow.cs`:

```csharp
namespace EMaigrator.Infrastructure.Data;

/// <summary>Encrypted credential blob. Purged the instant the owning job reaches a terminal state.</summary>
public class CredentialRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string SecretRef { get; set; } = "";
    public string CipherBlob { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
```

`src/EMaigrator.Infrastructure/Data/Tenant.cs`:

```csharp
namespace EMaigrator.Infrastructure.Data;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
```

`src/EMaigrator.Infrastructure/Data/ProviderIdValueConverter.cs`:

```csharp
using EMaigrator.Core.Model;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EMaigrator.Infrastructure.Data;

public sealed class ProviderIdValueConverter : ValueConverter<ProviderId, string>
{
    public ProviderIdValueConverter()
        : base(v => v.Value, v => new ProviderId(v)) { }
}
```

`src/EMaigrator.Infrastructure/Data/EmaigratorDbContext.cs`:

```csharp
using EMaigrator.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Data;

public class EmaigratorDbContext : DbContext
{
    public EmaigratorDbContext(DbContextOptions<EmaigratorDbContext> options) : base(options) { }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<MailboxMigration> MailboxMigrations => Set<MailboxMigration>();
    public DbSet<FolderTask> FolderTasks => Set<FolderTask>();
    public DbSet<LedgerEntryRow> LedgerEntries => Set<LedgerEntryRow>();
    public DbSet<MigrationLogRow> MigrationLogs => Set<MigrationLogRow>();
    public DbSet<CredentialRow> Credentials => Set<CredentialRow>();
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var providerIdConverter = new ProviderIdValueConverter();

        b.Entity<Job>(e =>
        {
            e.ToTable("jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceProvider).HasConversion(providerIdConverter).HasColumnType("text");
            e.Property(x => x.DestProvider).HasConversion(providerIdConverter).HasColumnType("text");
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<MailboxMigration>(e =>
        {
            e.ToTable("mailbox_migrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.JobId);
        });

        b.Entity<FolderTask>(e =>
        {
            e.ToTable("folder_tasks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MailboxMigrationId);
        });

        b.Entity<LedgerEntryRow>(e =>
        {
            e.ToTable("ledger_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.MailboxMigrationId, x.IdentityKey }).IsUnique();
        });

        b.Entity<MigrationLogRow>(e =>
        {
            e.ToTable("migration_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.HasIndex(x => x.MailboxMigrationId);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<CredentialRow>(e =>
        {
            e.ToTable("credentials");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SecretRef).IsUnique();
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
        });
    }
}
```

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~DbContextModelTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Data src/EMaigrator.Infrastructure.Tests/Data
git commit -m "feat(infra): add EF Core entities and EmaigratorDbContext per CONTRACTS

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Initial EF Core migration + Testcontainers Postgres apply test

**Goal:** Generate the initial EF Core migration for `EmaigratorDbContext` and prove it applies cleanly against a real PostgreSQL instance (Testcontainers), creating all tables and the ledger UNIQUE constraint.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Data/Migrations/<timestamp>_InitialCreate.cs` (generated)
- Create: `src/EMaigrator.Infrastructure/Data/Migrations/EmaigratorDbContextModelSnapshot.cs` (generated)
- Create: `src/EMaigrator.Infrastructure/Data/DesignTimeDbContextFactory.cs`
- Modify: `src/EMaigrator.Infrastructure.Tests/EMaigrator.Infrastructure.Tests.csproj` (add Testcontainers)
- Create: `src/EMaigrator.Infrastructure.Tests/Fixtures/PostgresFixture.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Data/MigrationApplyTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet ef migrations add InitialCreate` succeeds and the migration creates tables `jobs`, `mailbox_migrations`, `folder_tasks`, `ledger_entries`, `migration_logs`, `credentials`, `tenants`.
- [ ] A `DesignTimeDbContextFactory` lets `dotnet ef` build the context without a running app.
- [ ] An integration test spins up a `postgres:17-alpine` container, runs `Database.MigrateAsync()`, and queries `information_schema` to assert all seven tables exist and the ledger unique index exists.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~MigrationApplyTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Add the Testcontainers package to `src/EMaigrator.Infrastructure.Tests/EMaigrator.Infrastructure.Tests.csproj`:

```xml
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.0.0" />
    <PackageReference Include="Testcontainers.Redis" Version="4.0.0" />
    <PackageReference Include="Testcontainers.RabbitMq" Version="4.0.0" />
    <PackageReference Include="Npgsql" Version="10.0.0" />
```

Create the shared Postgres fixture `src/EMaigrator.Infrastructure.Tests/Fixtures/PostgresFixture.cs`:

```csharp
using Testcontainers.PostgreSql;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Fixtures;

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("emaigrator")
        .WithUsername("emaigrator")
        .WithPassword("emaigrator")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```

Create `src/EMaigrator.Infrastructure.Tests/Data/MigrationApplyTests.cs`:

```csharp
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Data;

[Collection("postgres")]
public class MigrationApplyTests
{
    private readonly PostgresFixture _pg;
    public MigrationApplyTests(PostgresFixture pg) => _pg = pg;

    private EmaigratorDbContext NewContext() =>
        new(new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options);

    [Fact]
    public async Task Migration_creates_all_tables()
    {
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", conn);
        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

        tables.Should().Contain(new[]
        {
            "jobs", "mailbox_migrations", "folder_tasks",
            "ledger_entries", "migration_logs", "credentials", "tenants"
        });
    }

    [Fact]
    public async Task Migration_creates_ledger_unique_index()
    {
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT indexdef FROM pg_indexes WHERE tablename = 'ledger_entries'", conn);
        var defs = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) defs.Add(reader.GetString(0));

        defs.Should().Contain(d =>
            d.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
            d.Contains("MailboxMigrationId", StringComparison.OrdinalIgnoreCase) &&
            d.Contains("IdentityKey", StringComparison.OrdinalIgnoreCase));
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~MigrationApplyTests` → FAILS: no migrations exist, `MigrateAsync` throws (no `__EFMigrationsHistory`/no migration assembly).

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Data/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EMaigrator.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EmaigratorDbContext>
{
    public EmaigratorDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("EMAIGRATOR_DESIGN_CS")
                 ?? "Host=localhost;Database=emaigrator;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<EmaigratorDbContext>()
            .UseNpgsql(cs, npg => npg.MigrationsAssembly("EMaigrator.Infrastructure"))
            .Options;
        return new EmaigratorDbContext(options);
    }
}
```

Generate the migration (run from repo root):

```
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef migrations add InitialCreate --project src/EMaigrator.Infrastructure/EMaigrator.Infrastructure.csproj --output-dir Data/Migrations
```

(Single line — no shell line-continuation, so it runs unchanged in both PowerShell and bash.)

This produces `Data/Migrations/<timestamp>_InitialCreate.cs` and `EmaigratorDbContextModelSnapshot.cs`. Confirm the generated `Up` contains `migrationBuilder.CreateTable(name: "ledger_entries", ...)` and a `CreateIndex(... unique: true)` on `MailboxMigrationId, IdentityKey`. Do not hand-edit the generated files.

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~MigrationApplyTests` → all pass (requires Docker available for Testcontainers).

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Data src/EMaigrator.Infrastructure.Tests
git commit -m "feat(infra): add initial EF migration with Testcontainers apply test

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: PostgresLedger implementing ILedger (Testcontainers)

**Goal:** Implement `PostgresLedger : ILedger` (`IsDoneAsync`/`MarkAsync`/`GetNotDoneAsync`/`GetCountsAsync`) as an idempotent upsert over `LedgerEntryRow`, proving against real Postgres that re-marking the same `(MailboxMigrationId, IdentityKey)` updates in place (no duplicate rows) and that counts/not-done queries are correct.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Persistence/PostgresLedger.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Persistence/PostgresLedgerTests.cs`

**Acceptance Criteria:**
- [ ] `PostgresLedger` implements `EMaigrator.Core.Abstractions.ILedger` verbatim.
- [ ] `MarkAsync` is idempotent: marking the same key twice leaves exactly one row whose `Status`/`ErrorCode`/`UpdatedAt` reflect the latest call (upsert via the unique index, not insert).
- [ ] `IsDoneAsync` returns true only for `Migrated` or `Skipped`.
- [ ] `GetNotDoneAsync` streams rows whose status is `Pending` or `Failed`.
- [ ] `GetCountsAsync` returns correct per-status counts.
- [ ] Integration tests prove all four behaviors against a Testcontainers Postgres.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~PostgresLedgerTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Persistence/PostgresLedgerTests.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Persistence;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Persistence;

[Collection("postgres")]
public class PostgresLedgerTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    public PostgresLedgerTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> Options =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private IDbContextFactory<EmaigratorDbContext> Factory => new TestContextFactory(Options);

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(Options);
        await ctx.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestContextFactory(DbContextOptions<EmaigratorDbContext> options)
        : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task MarkAsync_is_idempotent_upsert()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();

        await ledger.MarkAsync(mig, "mid:<a@x>", "INBOX", "Inbox", LedgerStatus.Pending, null, default);
        await ledger.MarkAsync(mig, "mid:<a@x>", "INBOX", "Inbox", LedgerStatus.Migrated, null, default);

        await using var ctx = new EmaigratorDbContext(Options);
        var rows = await ctx.LedgerEntries.Where(r => r.MailboxMigrationId == mig).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Status.Should().Be(LedgerStatus.Migrated);
    }

    [Fact]
    public async Task IsDoneAsync_true_only_for_migrated_or_skipped()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();

        await ledger.MarkAsync(mig, "k1", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "k2", "f", "f", LedgerStatus.Skipped, null, default);
        await ledger.MarkAsync(mig, "k3", "f", "f", LedgerStatus.Failed, "E1", default);
        await ledger.MarkAsync(mig, "k4", "f", "f", LedgerStatus.Pending, null, default);

        (await ledger.IsDoneAsync(mig, "k1", default)).Should().BeTrue();
        (await ledger.IsDoneAsync(mig, "k2", default)).Should().BeTrue();
        (await ledger.IsDoneAsync(mig, "k3", default)).Should().BeFalse();
        (await ledger.IsDoneAsync(mig, "k4", default)).Should().BeFalse();
        (await ledger.IsDoneAsync(mig, "missing", default)).Should().BeFalse();
    }

    [Fact]
    public async Task GetNotDoneAsync_returns_pending_and_failed()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();
        await ledger.MarkAsync(mig, "done", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "pend", "f", "f", LedgerStatus.Pending, null, default);
        await ledger.MarkAsync(mig, "fail", "f", "f", LedgerStatus.Failed, "E", default);

        var notDone = new List<string>();
        await foreach (var e in ledger.GetNotDoneAsync(mig, default)) notDone.Add(e.IdentityKey);

        notDone.Should().BeEquivalentTo(new[] { "pend", "fail" });
    }

    [Fact]
    public async Task GetCountsAsync_returns_per_status_counts()
    {
        var ledger = new PostgresLedger(Factory);
        var mig = Guid.NewGuid();
        await ledger.MarkAsync(mig, "a", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "b", "f", "f", LedgerStatus.Migrated, null, default);
        await ledger.MarkAsync(mig, "c", "f", "f", LedgerStatus.Skipped, null, default);
        await ledger.MarkAsync(mig, "d", "f", "f", LedgerStatus.Failed, "E", default);
        await ledger.MarkAsync(mig, "e", "f", "f", LedgerStatus.Pending, null, default);

        var counts = await ledger.GetCountsAsync(mig, default);
        counts.Should().Be(new LedgerCounts(Migrated: 2, Skipped: 1, Failed: 1, Pending: 1));
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~PostgresLedgerTests` → FAILS to compile: `PostgresLedger` does not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Persistence/PostgresLedger.cs`:

```csharp
using System.Runtime.CompilerServices;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed idempotency ledger. MarkAsync is an upsert keyed by the
/// UNIQUE(MailboxMigrationId, IdentityKey) index, so re-runs never create duplicate rows.
/// </summary>
public sealed class PostgresLedger : ILedger
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;

    public PostgresLedger(IDbContextFactory<EmaigratorDbContext> factory) => _factory = factory;

    public async Task<bool> IsDoneAsync(Guid mailboxMigrationId, string identityKey, CancellationToken ct)
    {
        await using var ctx = _factory.CreateDbContext();
        var status = await ctx.LedgerEntries
            .Where(r => r.MailboxMigrationId == mailboxMigrationId && r.IdentityKey == identityKey)
            .Select(r => (LedgerStatus?)r.Status)
            .FirstOrDefaultAsync(ct);
        return status is LedgerStatus.Migrated or LedgerStatus.Skipped;
    }

    public async Task MarkAsync(Guid mailboxMigrationId, string identityKey, string sourceFolder,
        string destFolder, LedgerStatus status, string? errorCode, CancellationToken ct)
    {
        await using var ctx = _factory.CreateDbContext();
        var existing = await ctx.LedgerEntries
            .FirstOrDefaultAsync(r => r.MailboxMigrationId == mailboxMigrationId && r.IdentityKey == identityKey, ct);

        if (existing is null)
        {
            ctx.LedgerEntries.Add(new LedgerEntryRow
            {
                MailboxMigrationId = mailboxMigrationId,
                IdentityKey = identityKey,
                SourceFolder = sourceFolder,
                DestFolder = destFolder,
                Status = status,
                ErrorCode = errorCode,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.SourceFolder = sourceFolder;
            existing.DestFolder = destFolder;
            existing.Status = status;
            existing.ErrorCode = errorCode;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Concurrent insert lost the race to the unique index — re-apply as update.
            ctx.ChangeTracker.Clear();
            var row = await ctx.LedgerEntries
                .FirstAsync(r => r.MailboxMigrationId == mailboxMigrationId && r.IdentityKey == identityKey, ct);
            row.SourceFolder = sourceFolder;
            row.DestFolder = destFolder;
            row.Status = status;
            row.ErrorCode = errorCode;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync(ct);
        }
    }

    public async IAsyncEnumerable<LedgerEntry> GetNotDoneAsync(
        Guid mailboxMigrationId, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var ctx = _factory.CreateDbContext();
        var query = ctx.LedgerEntries.AsNoTracking()
            .Where(r => r.MailboxMigrationId == mailboxMigrationId
                        && (r.Status == LedgerStatus.Pending || r.Status == LedgerStatus.Failed))
            .OrderBy(r => r.Id)
            .AsAsyncEnumerable();

        await foreach (var r in query.WithCancellation(ct))
        {
            yield return new LedgerEntry(r.MailboxMigrationId, r.IdentityKey, r.SourceFolder,
                r.DestFolder, r.Status, r.ErrorCode, r.UpdatedAt);
        }
    }

    public async Task<LedgerCounts> GetCountsAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        await using var ctx = _factory.CreateDbContext();
        var grouped = await ctx.LedgerEntries.AsNoTracking()
            .Where(r => r.MailboxMigrationId == mailboxMigrationId)
            .GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        long Get(LedgerStatus s) => grouped.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        return new LedgerCounts(Get(LedgerStatus.Migrated), Get(LedgerStatus.Skipped),
            Get(LedgerStatus.Failed), Get(LedgerStatus.Pending));
    }
}
```

Wire it in `DependencyInjection.cs` — add the DbContext factory and ledger registration inside `AddInfrastructure`, after the options binding:

```csharp
        services.AddDbContextFactory<Data.EmaigratorDbContext>((sp, b) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InfrastructureOptions>>().Value;
            b.UseNpgsql(opts.PostgresConnectionString,
                npg => npg.MigrationsAssembly("EMaigrator.Infrastructure"));
        });
        services.AddScoped<EMaigrator.Core.Abstractions.ILedger, Persistence.PostgresLedger>();
```

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~PostgresLedgerTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Persistence src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/Persistence
git commit -m "feat(infra): implement PostgresLedger with idempotent upsert

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: LocalKeyEnvelopeSecretStore (AES-GCM) implementing ISecretStore

**Goal:** Implement `LocalKeyEnvelopeSecretStore : ISecretStore` using AES-GCM envelope encryption (per-secret random data key wrapped by the configured master key) persisting `CredentialRow.CipherBlob` as ciphertext, proving round-trip Store→Retrieve, tamper detection, and that `CipherBlob` is never plaintext.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Secrets/EnvelopeCipher.cs`
- Create: `src/EMaigrator.Infrastructure/Secrets/IKeyWrapper.cs`
- Create: `src/EMaigrator.Infrastructure/Secrets/LocalKeyWrapper.cs`
- Create: `src/EMaigrator.Infrastructure/Secrets/LocalKeyEnvelopeSecretStore.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Secrets/LocalKeyEnvelopeSecretStoreTests.cs`

**Acceptance Criteria:**
- [ ] `LocalKeyEnvelopeSecretStore` implements `EMaigrator.Core.Abstractions.ISecretStore` verbatim (`StoreAsync`/`RetrieveAsync`/`PurgeAsync`).
- [ ] `StoreAsync` returns a `secretRef` and writes a `CredentialRow` whose `CipherBlob` contains neither the plaintext nor any substring of it.
- [ ] `RetrieveAsync(secretRef)` returns the original plaintext.
- [ ] Tampering with the ciphertext causes `RetrieveAsync` to throw (AES-GCM auth-tag failure).
- [ ] `PurgeAsync` deletes the row; subsequent `RetrieveAsync` throws.
- [ ] Wrapping uses a per-secret random 32-byte data key; the master key is read from `SecretStoreOptions.KeyRef` (base64, 32 bytes).

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~LocalKeyEnvelopeSecretStoreTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Secrets/LocalKeyEnvelopeSecretStoreTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Secrets;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Secrets;

[Collection("postgres")]
public class LocalKeyEnvelopeSecretStoreTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    public LocalKeyEnvelopeSecretStoreTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> o) : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(o);
    }

    private LocalKeyEnvelopeSecretStore NewStore()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var opts = Options.Create(new SecretStoreOptions { Mode = "LocalKey", KeyRef = key });
        return new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new LocalKeyWrapper(opts), new EnvelopeCipher());
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Store_then_retrieve_roundtrips()
    {
        var store = NewStore();
        var tenant = Guid.NewGuid().ToString();
        const string secret = "imap-app-password-Sup3rSecret!";

        var secretRef = await store.StoreAsync(tenant, secret, default);
        secretRef.Should().NotBeNullOrWhiteSpace();

        var back = await store.RetrieveAsync(secretRef, default);
        back.Should().Be(secret);
    }

    [Fact]
    public async Task Stored_blob_is_ciphertext_not_plaintext()
    {
        var store = NewStore();
        const string secret = "PLAINTEXT-CANARY-9f2a";
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), secret, default);

        await using var ctx = new EmaigratorDbContext(DbOptions);
        var row = await ctx.Credentials.SingleAsync(r => r.SecretRef == secretRef);

        row.CipherBlob.Should().NotContain(secret);
        Encoding.UTF8.GetString(Convert.FromBase64String(row.CipherBlob))
            .Should().NotContain(secret);
    }

    [Fact]
    public async Task Tampered_ciphertext_fails_to_decrypt()
    {
        var store = NewStore();
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "secret", default);

        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            var row = await ctx.Credentials.SingleAsync(r => r.SecretRef == secretRef);
            var bytes = Convert.FromBase64String(row.CipherBlob);
            bytes[^1] ^= 0xFF; // flip a tag byte
            row.CipherBlob = Convert.ToBase64String(bytes);
            await ctx.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(
            () => store.RetrieveAsync(secretRef, default));
    }

    [Fact]
    public async Task Purge_removes_secret()
    {
        var store = NewStore();
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "secret", default);

        await store.PurgeAsync(secretRef, default);

        await using var ctx = new EmaigratorDbContext(DbOptions);
        (await ctx.Credentials.AnyAsync(r => r.SecretRef == secretRef)).Should().BeFalse();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RetrieveAsync(secretRef, default));
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~LocalKeyEnvelopeSecretStoreTests` → FAILS to compile: `EnvelopeCipher`, `LocalKeyWrapper`, `LocalKeyEnvelopeSecretStore` do not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Secrets/IKeyWrapper.cs`:

```csharp
namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// Wraps/unwraps a per-secret data key with a master key. Local impl wraps in-process;
/// the KMS impl delegates to Azure Key Vault / AWS KMS.
/// </summary>
public interface IKeyWrapper
{
    Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct);
    Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken ct);
}
```

Create `src/EMaigrator.Infrastructure/Secrets/EnvelopeCipher.cs`:

```csharp
using System.Security.Cryptography;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// AES-256-GCM data-key encryption of a plaintext payload. Output layout:
/// [4-byte wrappedKeyLen][wrappedKey][12-byte nonce][16-byte tag][ciphertext].
/// </summary>
public sealed class EnvelopeCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DataKeySize = 32;

    public byte[] GenerateDataKey() => RandomNumberGenerator.GetBytes(DataKeySize);

    public byte[] Seal(byte[] dataKey, byte[] wrappedDataKey, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(dataKey, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(wrappedDataKey.Length);
        w.Write(wrappedDataKey);
        w.Write(nonce);
        w.Write(tag);
        w.Write(ciphertext);
        w.Flush();
        return ms.ToArray();
    }

    public (byte[] wrappedDataKey, byte[] payload) ExtractWrappedKey(byte[] blob)
    {
        using var ms = new MemoryStream(blob);
        using var r = new BinaryReader(ms);
        var len = r.ReadInt32();
        var wrapped = r.ReadBytes(len);
        var rest = r.ReadBytes(blob.Length - 4 - len);
        return (wrapped, rest);
    }

    public byte[] Open(byte[] dataKey, byte[] payload)
    {
        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var ciphertext = payload[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];
        using var gcm = new AesGcm(dataKey, TagSize);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
```

Create `src/EMaigrator.Infrastructure/Secrets/LocalKeyWrapper.cs`:

```csharp
using System.Security.Cryptography;
using EMaigrator.Core.Configuration;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>Wraps the data key with a config-provided 32-byte AES master key (self-host mode).</summary>
public sealed class LocalKeyWrapper : IKeyWrapper
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _masterKey;

    public LocalKeyWrapper(IOptions<SecretStoreOptions> options)
    {
        var keyRef = options.Value.KeyRef
            ?? throw new InvalidOperationException("SecretStore:KeyRef is required for LocalKey mode.");
        _masterKey = Convert.FromBase64String(keyRef);
        if (_masterKey.Length != 32)
            throw new InvalidOperationException("SecretStore:KeyRef must be a base64-encoded 32-byte key.");
    }

    public Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(_masterKey, TagSize);
        gcm.Encrypt(nonce, dataKey, ciphertext, tag);
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return Task.FromResult(result);
    }

    public Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken ct)
    {
        var nonce = wrappedDataKey[..NonceSize];
        var tag = wrappedDataKey[NonceSize..(NonceSize + TagSize)];
        var ciphertext = wrappedDataKey[(NonceSize + TagSize)..];
        var dataKey = new byte[ciphertext.Length];
        using var gcm = new AesGcm(_masterKey, TagSize);
        gcm.Decrypt(nonce, ciphertext, tag, dataKey);
        return Task.FromResult(dataKey);
    }
}
```

Create `src/EMaigrator.Infrastructure/Secrets/LocalKeyEnvelopeSecretStore.cs`:

```csharp
using System.Text;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// Envelope-encrypting credential store. A random data key encrypts the secret (AES-GCM); the data
/// key is wrapped by the master key via IKeyWrapper. Only the ciphertext envelope is persisted —
/// a DB breach yields ciphertext, never plaintext.
/// </summary>
public sealed class LocalKeyEnvelopeSecretStore : ISecretStore
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;
    private readonly IKeyWrapper _wrapper;
    private readonly EnvelopeCipher _cipher;

    public LocalKeyEnvelopeSecretStore(IDbContextFactory<EmaigratorDbContext> factory,
        IKeyWrapper wrapper, EnvelopeCipher cipher)
    {
        _factory = factory;
        _wrapper = wrapper;
        _cipher = cipher;
    }

    public async Task<string> StoreAsync(string tenantId, string plaintext, CancellationToken ct)
    {
        var dataKey = _cipher.GenerateDataKey();
        var wrapped = await _wrapper.WrapAsync(dataKey, ct);
        var blob = _cipher.Seal(dataKey, wrapped, Encoding.UTF8.GetBytes(plaintext));
        Array.Clear(dataKey);

        var secretRef = $"cred:{Guid.NewGuid():N}";
        await using var dbctx = _factory.CreateDbContext();
        dbctx.Credentials.Add(new CredentialRow
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.TryParse(tenantId, out var t) ? t : Guid.Empty,
            SecretRef = secretRef,
            CipherBlob = Convert.ToBase64String(blob),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await dbctx.SaveChangesAsync(ct);
        return secretRef;
    }

    public async Task<string> RetrieveAsync(string secretRef, CancellationToken ct)
    {
        await using var dbctx = _factory.CreateDbContext();
        var row = await dbctx.Credentials.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SecretRef == secretRef, ct)
            ?? throw new KeyNotFoundException($"No credential for secretRef '{secretRef}'.");

        var blob = Convert.FromBase64String(row.CipherBlob);
        var (wrapped, payload) = _cipher.ExtractWrappedKey(blob);
        var dataKey = await _wrapper.UnwrapAsync(wrapped, ct);
        try
        {
            return Encoding.UTF8.GetString(_cipher.Open(dataKey, payload));
        }
        finally
        {
            Array.Clear(dataKey);
        }
    }

    public async Task PurgeAsync(string secretRef, CancellationToken ct)
    {
        await using var dbctx = _factory.CreateDbContext();
        await dbctx.Credentials.Where(r => r.SecretRef == secretRef).ExecuteDeleteAsync(ct);
    }
}
```

Register in `DependencyInjection.cs` (mode-switched):

```csharp
        services.AddSingleton<Secrets.EnvelopeCipher>();
        services.AddSingleton<EMaigrator.Core.Abstractions.ISecretStore>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InfrastructureOptions>>().Value;
            var factory = sp.GetRequiredService<IDbContextFactory<Data.EmaigratorDbContext>>();
            var cipher = sp.GetRequiredService<Secrets.EnvelopeCipher>();
            var ssOptions = Microsoft.Extensions.Options.Options.Create(opts.SecretStore);
            Secrets.IKeyWrapper wrapper = opts.SecretStore.Mode switch
            {
                "AzureKeyVault" => sp.GetRequiredService<Secrets.KmsKeyWrapper>(),
                _ => new Secrets.LocalKeyWrapper(ssOptions),
            };
            return new Secrets.LocalKeyEnvelopeSecretStore(factory, wrapper, cipher);
        });
```

(`KmsKeyWrapper` is added in Task 6; the switch already references it so registration order is correct.)

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~LocalKeyEnvelopeSecretStoreTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Secrets src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/Secrets
git commit -m "feat(infra): add AES-GCM envelope LocalKeyEnvelopeSecretStore

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: KMS envelope seam — KmsKeyWrapper + Azure Key Vault impl with faked KMS

**Goal:** Add the KMS envelope seam: an `IKmsClient` abstraction, a `KmsKeyWrapper` that wraps/unwraps the data key through it, and an `AzureKeyVaultKmsClient` skeleton — proving the secret store works end-to-end against a **faked** KMS (in-memory `IKmsClient`) so credentials encrypt with a wrapped key the local process never holds.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Secrets/IKmsClient.cs`
- Create: `src/EMaigrator.Infrastructure/Secrets/KmsKeyWrapper.cs`
- Create: `src/EMaigrator.Infrastructure/Secrets/AzureKeyVaultKmsClient.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Secrets/KmsEnvelopeSecretStoreTests.cs`

**Acceptance Criteria:**
- [ ] `IKmsClient` exposes `WrapKeyAsync(byte[])`/`UnwrapKeyAsync(byte[])`; `KmsKeyWrapper` implements `IKeyWrapper` by delegating to it.
- [ ] `AzureKeyVaultKmsClient` constructs a `KeyClient` (`Azure.Security.KeyVault.Keys`) from a vault URI + `DefaultAzureCredential` and implements wrap/unwrap via `CryptographyClient` (skeleton compiles; not invoked in unit tests).
- [ ] A `FakeKmsClient` (test double) XOR-wraps with a fixed in-memory key; using it, `LocalKeyEnvelopeSecretStore` round-trips Store→Retrieve→Purge against Testcontainers Postgres, and `CipherBlob` stays ciphertext.
- [ ] `AddInfrastructure` registers `KmsKeyWrapper` + `AzureKeyVaultKmsClient` when `SecretStore.Mode == "AzureKeyVault"`.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~KmsEnvelopeSecretStoreTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Secrets/KmsEnvelopeSecretStoreTests.cs`:

```csharp
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Secrets;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Secrets;

[Collection("postgres")]
public class KmsEnvelopeSecretStoreTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    public KmsEnvelopeSecretStoreTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> o) : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(o);
    }

    /// <summary>In-memory KMS double: deterministic reversible wrap so the local process never holds the master key.</summary>
    private sealed class FakeKmsClient : IKmsClient
    {
        private readonly byte[] _kek = Enumerable.Range(0, 32).Select(i => (byte)(i * 7 + 3)).ToArray();
        public int WrapCalls { get; private set; }
        public Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct)
        {
            WrapCalls++;
            return Task.FromResult(key.Select((b, i) => (byte)(b ^ _kek[i % _kek.Length])).ToArray());
        }
        public Task<byte[]> UnwrapKeyAsync(byte[] wrapped, CancellationToken ct) =>
            Task.FromResult(wrapped.Select((b, i) => (byte)(b ^ _kek[i % _kek.Length])).ToArray());
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Store_retrieve_roundtrips_through_kms_wrapper()
    {
        var kms = new FakeKmsClient();
        var store = new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new KmsKeyWrapper(kms), new EnvelopeCipher());

        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "kms-protected-secret", default);
        (await store.RetrieveAsync(secretRef, default)).Should().Be("kms-protected-secret");
        kms.WrapCalls.Should().Be(1, "the data key is wrapped exactly once per stored secret");
    }

    [Fact]
    public async Task Blob_remains_ciphertext_under_kms_wrapping()
    {
        var kms = new FakeKmsClient();
        var store = new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new KmsKeyWrapper(kms), new EnvelopeCipher());
        const string secret = "KMS-CANARY-771a";

        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), secret, default);

        await using var ctx = new EmaigratorDbContext(DbOptions);
        var row = await ctx.Credentials.SingleAsync(r => r.SecretRef == secretRef);
        row.CipherBlob.Should().NotContain(secret);
    }

    [Fact]
    public async Task Purge_then_retrieve_throws()
    {
        var store = new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new KmsKeyWrapper(new FakeKmsClient()), new EnvelopeCipher());
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "s", default);
        await store.PurgeAsync(secretRef, default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RetrieveAsync(secretRef, default));
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~KmsEnvelopeSecretStoreTests` → FAILS to compile: `IKmsClient`, `KmsKeyWrapper` do not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Secrets/IKmsClient.cs`:

```csharp
namespace EMaigrator.Infrastructure.Secrets;

/// <summary>Managed-KMS key-wrapping seam (Azure Key Vault / AWS KMS). The master key never leaves the KMS.</summary>
public interface IKmsClient
{
    Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct);
    Task<byte[]> UnwrapKeyAsync(byte[] wrapped, CancellationToken ct);
}
```

Create `src/EMaigrator.Infrastructure/Secrets/KmsKeyWrapper.cs`:

```csharp
namespace EMaigrator.Infrastructure.Secrets;

/// <summary>Wraps the per-secret data key through a managed KMS (envelope encryption, hosted mode).</summary>
public sealed class KmsKeyWrapper : IKeyWrapper
{
    private readonly IKmsClient _kms;
    public KmsKeyWrapper(IKmsClient kms) => _kms = kms;

    public Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct) => _kms.WrapKeyAsync(dataKey, ct);
    public Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken ct) => _kms.UnwrapKeyAsync(wrappedDataKey, ct);
}
```

Create `src/EMaigrator.Infrastructure/Secrets/AzureKeyVaultKmsClient.cs`:

```csharp
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using EMaigrator.Core.Configuration;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// Azure Key Vault KMS client. KeyRef is the full key identifier
/// (e.g. https://&lt;vault&gt;.vault.azure.net/keys/&lt;name&gt;). Wrap/unwrap run inside Key Vault
/// via RSA-OAEP; the master key never leaves the vault.
/// </summary>
public sealed class AzureKeyVaultKmsClient : IKmsClient
{
    private readonly CryptographyClient _crypto;

    public AzureKeyVaultKmsClient(IOptions<SecretStoreOptions> options)
    {
        var keyId = options.Value.KeyRef
            ?? throw new InvalidOperationException("SecretStore:KeyRef (Key Vault key id) is required for AzureKeyVault mode.");
        _crypto = new CryptographyClient(new Uri(keyId), new DefaultAzureCredential());
    }

    public async Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct)
    {
        var result = await _crypto.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, key, ct);
        return result.EncryptedKey;
    }

    public async Task<byte[]> UnwrapKeyAsync(byte[] wrapped, CancellationToken ct)
    {
        var result = await _crypto.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrapped, ct);
        return result.Key;
    }
}
```

Add the Key Vault Keys package to the project (so `CryptographyClient` resolves) — add to `EMaigrator.Infrastructure.csproj`:

```xml
    <PackageReference Include="Azure.Security.KeyVault.Keys" Version="4.7.0" />
```

Register KMS in `DependencyInjection.cs` (gate by mode so DefaultAzureCredential isn't constructed in LocalKey mode):

```csharp
        var ssMode = config.GetSection(InfrastructureOptions.SectionName)["SecretStore:Mode"] ?? "LocalKey";
        if (ssMode == "AzureKeyVault")
        {
            services.AddSingleton<Secrets.IKmsClient, Secrets.AzureKeyVaultKmsClient>();
            services.AddSingleton<Secrets.KmsKeyWrapper>();
        }
```

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~KmsEnvelopeSecretStoreTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Secrets src/EMaigrator.Infrastructure/EMaigrator.Infrastructure.csproj src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/Secrets
git commit -m "feat(infra): add KMS envelope seam with Azure Key Vault client and faked-KMS tests

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: RedisRateLimiter — atomic Lua token bucket (TryAcquire/Penalize)

**Goal:** Implement `RedisRateLimiter : IRateLimiter` with `TryAcquireAsync`/`PenalizeAsync` backed by an atomic Lua token-bucket script in Redis, proving against Testcontainers Redis that tokens deplete and refill correctly, that an empty bucket returns `false`, and that `PenalizeAsync` honors `Retry-After` by pausing only that key's bucket.

**Files:**
- Create: `src/EMaigrator.Infrastructure/RateLimiting/TokenBucketScripts.cs`
- Create: `src/EMaigrator.Infrastructure/RateLimiting/RedisRateLimiter.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Fixtures/RedisFixture.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/RateLimiting/RedisRateLimiterTests.cs`

**Acceptance Criteria:**
- [ ] `RedisRateLimiter` implements `EMaigrator.Core.Abstractions.IRateLimiter` verbatim.
- [ ] `TryAcquireAsync` evaluates a single Lua script (atomic refill + decrement) keyed by `rl:{provider}:{account}`.
- [ ] A bucket with burst N grants N acquisitions then returns `false`; after waiting `tokens/refillPerSecond` seconds it grants again.
- [ ] `PenalizeAsync(key, retryAfter)` sets a penalty marker so `TryAcquireAsync` returns `false` for that key (and only that key) until the penalty expires.
- [ ] Bucket spec (`RefillPerSecond`, `Burst`) is resolved from `RateLimitOptions.Buckets` keyed by `"{provider}:{account-class}"`, with a default fallback bucket.
- [ ] All behaviors proven against Testcontainers Redis.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~RedisRateLimiterTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Fixtures/RedisFixture.cs`:

```csharp
using Testcontainers.Redis;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Fixtures;

public sealed class RedisFixture : IAsyncLifetime
{
    public RedisContainer Container { get; } = new RedisBuilder()
        .WithImage("redis:8-alpine")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }
```

Create `src/EMaigrator.Infrastructure.Tests/RateLimiting/RedisRateLimiterTests.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.RateLimiting;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.RateLimiting;

[Collection("redis")]
public class RedisRateLimiterTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer _mux = null!;
    public RedisRateLimiterTests(RedisFixture redis) => _redis = redis;

    public async Task InitializeAsync() => _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
    public async Task DisposeAsync() => await _mux.DisposeAsync();

    private RedisRateLimiter NewLimiter(double refillPerSecond, int burst)
    {
        var opts = Options.Create(new RateLimitOptions
        {
            Buckets = new() { ["default"] = new BucketSpec { RefillPerSecond = refillPerSecond, Burst = burst } }
        });
        return new RedisRateLimiter(_mux, opts);
    }

    private static RateLimitKey Key() => new(new ProviderId("graph"), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Grants_up_to_burst_then_throttles()
    {
        var limiter = NewLimiter(refillPerSecond: 0.001, burst: 3);
        var key = Key();

        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeFalse("burst exhausted, refill negligible");
    }

    [Fact]
    public async Task Refills_over_time()
    {
        var limiter = NewLimiter(refillPerSecond: 100, burst: 1);
        var key = Key();

        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeFalse();

        await Task.Delay(200); // 100 tok/s * 0.2s = 20 tokens (capped at burst)
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue("bucket refilled");
    }

    [Fact]
    public async Task Penalize_blocks_only_that_key_until_retry_after()
    {
        var limiter = NewLimiter(refillPerSecond: 1000, burst: 100);
        var penalized = Key();
        var other = Key();

        await limiter.PenalizeAsync(penalized, TimeSpan.FromMilliseconds(400), default);

        (await limiter.TryAcquireAsync(penalized, 1, default)).Should().BeFalse("under penalty");
        (await limiter.TryAcquireAsync(other, 1, default)).Should().BeTrue("other account unaffected");

        await Task.Delay(500);
        (await limiter.TryAcquireAsync(penalized, 1, default)).Should().BeTrue("penalty expired");
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~RedisRateLimiterTests` → FAILS to compile: `RedisRateLimiter` does not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/RateLimiting/TokenBucketScripts.cs`:

```csharp
namespace EMaigrator.Infrastructure.RateLimiting;

internal static class TokenBucketScripts
{
    // KEYS[1] = bucket hash key, KEYS[2] = penalty key
    // ARGV[1] = refillPerSecond, ARGV[2] = burst, ARGV[3] = nowMs, ARGV[4] = requestedTokens
    // Returns 1 if granted, 0 if throttled.
    public const string Acquire = @"
if redis.call('EXISTS', KEYS[2]) == 1 then
  return 0
end
local refill = tonumber(ARGV[1])
local burst = tonumber(ARGV[2])
local now = tonumber(ARGV[3])
local requested = tonumber(ARGV[4])
local data = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(data[1])
local ts = tonumber(data[2])
if tokens == nil then
  tokens = burst
  ts = now
end
local elapsed = (now - ts) / 1000.0
if elapsed < 0 then elapsed = 0 end
tokens = math.min(burst, tokens + elapsed * refill)
local granted = 0
if tokens >= requested then
  tokens = tokens - requested
  granted = 1
end
redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now)
redis.call('PEXPIRE', KEYS[1], 3600000)
return granted
";
}
```

Create `src/EMaigrator.Infrastructure/RateLimiting/RedisRateLimiter.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EMaigrator.Infrastructure.RateLimiting;

/// <summary>
/// Distributed per-(provider, account) token bucket in Redis. TryAcquireAsync runs an atomic Lua
/// script (refill + decrement); PenalizeAsync sets a per-key penalty so a 429/Retry-After pauses
/// only that account's bucket while all other accounts keep flowing.
/// </summary>
public sealed class RedisRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _mux;
    private readonly RateLimitOptions _options;

    public RedisRateLimiter(IConnectionMultiplexer mux, IOptions<RateLimitOptions> options)
    {
        _mux = mux;
        _options = options.Value;
    }

    private static string BucketKey(RateLimitKey k) => $"rl:{k.Provider.Value}:{k.Account}";
    private static string PenaltyKey(RateLimitKey k) => $"rlp:{k.Provider.Value}:{k.Account}";

    private BucketSpec Resolve(RateLimitKey k)
    {
        if (_options.Buckets.TryGetValue($"{k.Provider.Value}:{k.Account}", out var exact)) return exact;
        if (_options.Buckets.TryGetValue(k.Provider.Value, out var byProvider)) return byProvider;
        if (_options.Buckets.TryGetValue("default", out var def)) return def;
        return new BucketSpec { RefillPerSecond = 10, Burst = 20 };
    }

    public async Task<bool> TryAcquireAsync(RateLimitKey key, int tokens, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        var spec = Resolve(key);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = await db.ScriptEvaluateAsync(
            TokenBucketScripts.Acquire,
            new RedisKey[] { BucketKey(key), PenaltyKey(key) },
            new RedisValue[]
            {
                spec.RefillPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture),
                spec.Burst.ToString(System.Globalization.CultureInfo.InvariantCulture),
                now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        return (long)result == 1;
    }

    public async Task PenalizeAsync(RateLimitKey key, TimeSpan retryAfter, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        await db.StringSetAsync(PenaltyKey(key), "1", retryAfter);
    }
}
```

Register in `DependencyInjection.cs`:

```csharp
        services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InfrastructureOptions>>().Value;
            return StackExchange.Redis.ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        });
        services.Configure<EMaigrator.Core.Configuration.RateLimitOptions>(o =>
        {
            var opts = config.GetSection(InfrastructureOptions.SectionName)
                .Get<InfrastructureOptions>() ?? new InfrastructureOptions();
            o.Buckets = opts.RateLimit.Buckets;
        });
        services.AddSingleton<EMaigrator.Core.Abstractions.IRateLimiter, RateLimiting.RedisRateLimiter>();
```

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~RedisRateLimiterTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/RateLimiting src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/RateLimiting src/EMaigrator.Infrastructure.Tests/Fixtures/RedisFixture.cs
git commit -m "feat(infra): add Redis Lua token-bucket RedisRateLimiter

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: AIMD adaptive backoff on the rate limiter

**Goal:** Extend the rate limiter with AIMD adaptive backoff — repeated penalties multiplicatively decrease a key's effective refill rate; sustained success additively recovers it toward the configured cap — proving the effective rate drops after consecutive 429s and recovers after a quiet period.

**Files:**
- Modify: `src/EMaigrator.Infrastructure/RateLimiting/TokenBucketScripts.cs`
- Modify: `src/EMaigrator.Infrastructure/RateLimiting/RedisRateLimiter.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/RateLimiting/AimdBackoffTests.cs`

**Acceptance Criteria:**
- [ ] Each key tracks an effective refill multiplier in Redis (`0 < m <= 1`), persisted alongside the bucket.
- [ ] `PenalizeAsync` multiplies the effective multiplier by a decrease factor (default 0.5), floored at a minimum (default 0.05).
- [ ] Each granted `TryAcquireAsync` additively increases the multiplier toward 1.0 (default +0.02 per grant).
- [ ] The acquire Lua script applies `effectiveRefill = refillPerSecond * multiplier`.
- [ ] A test shows: after N consecutive penalties the multiplier is measurably lower (fewer grants in a fixed window) and after many successful grants it recovers toward 1.0.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~AimdBackoffTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/RateLimiting/AimdBackoffTests.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.RateLimiting;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.RateLimiting;

[Collection("redis")]
public class AimdBackoffTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer _mux = null!;
    public AimdBackoffTests(RedisFixture redis) => _redis = redis;

    public async Task InitializeAsync() => _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
    public async Task DisposeAsync() => await _mux.DisposeAsync();

    private RedisRateLimiter NewLimiter() => new(_mux, Options.Create(new RateLimitOptions
    {
        Buckets = new() { ["default"] = new BucketSpec { RefillPerSecond = 100, Burst = 1 } }
    }));

    [Fact]
    public async Task Multiplier_decreases_on_penalty_and_recovers_on_success()
    {
        var limiter = NewLimiter();
        var key = new RateLimitKey(new ProviderId("graph"), Guid.NewGuid().ToString("N"));

        (await limiter.GetEffectiveMultiplierAsync(key)).Should().Be(1.0);

        await limiter.PenalizeAsync(key, TimeSpan.FromMilliseconds(1), default);
        await limiter.PenalizeAsync(key, TimeSpan.FromMilliseconds(1), default);
        var afterPenalties = await limiter.GetEffectiveMultiplierAsync(key);
        afterPenalties.Should().BeApproximately(0.25, 0.001, "two halvings: 1 -> 0.5 -> 0.25");

        await Task.Delay(5);
        for (var i = 0; i < 100; i++)
        {
            if (await limiter.TryAcquireAsync(key, 1, default)) { }
            await Task.Delay(2); // let bucket refill so grants continue
        }

        var recovered = await limiter.GetEffectiveMultiplierAsync(key);
        recovered.Should().BeGreaterThan(afterPenalties, "additive recovery on sustained success");
    }

    [Fact]
    public async Task Multiplier_is_floored()
    {
        var limiter = NewLimiter();
        var key = new RateLimitKey(new ProviderId("graph"), Guid.NewGuid().ToString("N"));
        for (var i = 0; i < 20; i++)
            await limiter.PenalizeAsync(key, TimeSpan.FromMilliseconds(1), default);

        (await limiter.GetEffectiveMultiplierAsync(key)).Should().BeGreaterThanOrEqualTo(0.05);
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~AimdBackoffTests` → FAILS to compile: `GetEffectiveMultiplierAsync` does not exist.

3. - [ ] **Minimal implementation.** Replace the `Acquire` script in `TokenBucketScripts.cs` so it reads/updates the multiplier and additively recovers on grant:

```csharp
namespace EMaigrator.Infrastructure.RateLimiting;

internal static class TokenBucketScripts
{
    // KEYS[1] = bucket hash key, KEYS[2] = penalty key
    // ARGV: 1 refillPerSecond, 2 burst, 3 nowMs, 4 requested, 5 additiveIncrease
    public const string Acquire = @"
if redis.call('EXISTS', KEYS[2]) == 1 then
  return 0
end
local refill = tonumber(ARGV[1])
local burst = tonumber(ARGV[2])
local now = tonumber(ARGV[3])
local requested = tonumber(ARGV[4])
local inc = tonumber(ARGV[5])
local data = redis.call('HMGET', KEYS[1], 'tokens', 'ts', 'mult')
local tokens = tonumber(data[1])
local ts = tonumber(data[2])
local mult = tonumber(data[3])
if mult == nil then mult = 1.0 end
if tokens == nil then tokens = burst; ts = now end
local elapsed = (now - ts) / 1000.0
if elapsed < 0 then elapsed = 0 end
tokens = math.min(burst, tokens + elapsed * refill * mult)
local granted = 0
if tokens >= requested then
  tokens = tokens - requested
  granted = 1
  mult = math.min(1.0, mult + inc)
end
redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now, 'mult', mult)
redis.call('PEXPIRE', KEYS[1], 3600000)
return granted
";

    // KEYS[1] = bucket hash key. ARGV: 1 decreaseFactor, 2 floor.
    public const string Penalize = @"
local data = redis.call('HMGET', KEYS[1], 'mult')
local mult = tonumber(data[1])
if mult == nil then mult = 1.0 end
mult = math.max(tonumber(ARGV[2]), mult * tonumber(ARGV[1]))
redis.call('HSET', KEYS[1], 'mult', mult)
redis.call('PEXPIRE', KEYS[1], 3600000)
return tostring(mult)
";
}
```

Update `RedisRateLimiter.cs` to pass the additive-increase arg, apply the multiplicative decrease in `PenalizeAsync`, and add the inspection helper:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EMaigrator.Infrastructure.RateLimiting;

public sealed class RedisRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _mux;
    private readonly RateLimitOptions _options;

    private const double AdditiveIncrease = 0.02;
    private const double DecreaseFactor = 0.5;
    private const double MultiplierFloor = 0.05;

    public RedisRateLimiter(IConnectionMultiplexer mux, IOptions<RateLimitOptions> options)
    {
        _mux = mux;
        _options = options.Value;
    }

    private static string BucketKey(RateLimitKey k) => $"rl:{k.Provider.Value}:{k.Account}";
    private static string PenaltyKey(RateLimitKey k) => $"rlp:{k.Provider.Value}:{k.Account}";
    private static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private BucketSpec Resolve(RateLimitKey k)
    {
        if (_options.Buckets.TryGetValue($"{k.Provider.Value}:{k.Account}", out var exact)) return exact;
        if (_options.Buckets.TryGetValue(k.Provider.Value, out var byProvider)) return byProvider;
        if (_options.Buckets.TryGetValue("default", out var def)) return def;
        return new BucketSpec { RefillPerSecond = 10, Burst = 20 };
    }

    public async Task<bool> TryAcquireAsync(RateLimitKey key, int tokens, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        var spec = Resolve(key);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = await db.ScriptEvaluateAsync(
            TokenBucketScripts.Acquire,
            new RedisKey[] { BucketKey(key), PenaltyKey(key) },
            new RedisValue[] { Inv(spec.RefillPerSecond), Inv(spec.Burst), Inv(now), Inv(tokens), Inv(AdditiveIncrease) });
        return (long)result == 1;
    }

    public async Task PenalizeAsync(RateLimitKey key, TimeSpan retryAfter, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        await db.StringSetAsync(PenaltyKey(key), "1", retryAfter < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : retryAfter);
        await db.ScriptEvaluateAsync(
            TokenBucketScripts.Penalize,
            new RedisKey[] { BucketKey(key) },
            new RedisValue[] { Inv(DecreaseFactor), Inv(MultiplierFloor) });
    }

    /// <summary>Test/observability helper: reads the current AIMD effective-refill multiplier for a key.</summary>
    public async Task<double> GetEffectiveMultiplierAsync(RateLimitKey key)
    {
        var db = _mux.GetDatabase();
        var v = await db.HashGetAsync(BucketKey(key), "mult");
        return v.IsNull ? 1.0 : (double)v;
    }
}
```

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~AimdBackoffTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/RateLimiting src/EMaigrator.Infrastructure.Tests/RateLimiting/AimdBackoffTests.cs
git commit -m "feat(infra): add AIMD adaptive backoff to RedisRateLimiter

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: MassTransit/RabbitMQ wiring + MassTransitJobOrchestrator with DLQ

**Goal:** Wire MassTransit over RabbitMQ (consumer prefetch, DLQ/redelivery from `OrchestrationOptions`) and implement `MassTransitJobOrchestrator : IJobOrchestrator`, proving against Testcontainers RabbitMQ that publishing `StartMigration` reaches a registered consumer and that a consumer throwing repeatedly parks the message in the error/dead-letter queue.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Messaging/MassTransitJobOrchestrator.cs`
- Create: `src/EMaigrator.Infrastructure/Messaging/MassTransitConfig.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Fixtures/RabbitMqFixture.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Messaging/MassTransitOrchestratorTests.cs`

**Acceptance Criteria:**
- [ ] `MassTransitJobOrchestrator` implements `EMaigrator.Core.Abstractions.IJobOrchestrator` (`EnqueueMigrationAsync` publishes `StartMigration`; pause/resume/cancel publish their control contracts).
- [ ] `MassTransitConfig.AddEmaigratorMessaging` configures the RabbitMQ host from the connection string, sets `PrefetchCount`/`ConcurrentMessageLimit` from `OrchestrationOptions`, and configures redelivery + a move-to-error (DLQ) policy with `DlqRetryCount`.
- [ ] An integration test publishes `StartMigration` and a test consumer receives it (correlated by `MailboxMigrationId`).
- [ ] An integration test with an always-throwing consumer shows the message lands in the `_error` queue after the configured retries (asserted via RabbitMQ management or by binding a fault/error consumer).
- [ ] No message contract is redefined — uses `EMaigrator.Core.Contracts.StartMigration` etc. verbatim.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~MassTransitOrchestratorTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Fixtures/RabbitMqFixture.cs`:

```csharp
using Testcontainers.RabbitMq;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Fixtures;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    public RabbitMqContainer Container { get; } = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("rabbitmq")]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture> { }
```

Create `src/EMaigrator.Infrastructure.Tests/Messaging/MassTransitOrchestratorTests.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Contracts;
using EMaigrator.Infrastructure.Messaging;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Messaging;

[Collection("rabbitmq")]
public class MassTransitOrchestratorTests
{
    private readonly RabbitMqFixture _rabbit;
    public MassTransitOrchestratorTests(RabbitMqFixture rabbit) => _rabbit = rabbit;

    public sealed class Received
    {
        public TaskCompletionSource<Guid> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class StartConsumer(Received received) : IConsumer<StartMigration>
    {
        public Task Consume(ConsumeContext<StartMigration> ctx)
        {
            received.Tcs.TrySetResult(ctx.Message.MailboxMigrationId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Enqueue_publishes_to_a_consumer()
    {
        var received = new Received();
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.Configure<OrchestrationOptions>(_ => { });
        services.AddMassTransit(x =>
        {
            x.AddConsumer<StartConsumer>();
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(_rabbit.ConnectionString));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        await using var sp = services.BuildServiceProvider(true);
        var bus = sp.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            var orchestrator = new MassTransitJobOrchestrator(sp.GetRequiredService<IPublishEndpoint>(),
                sp.GetRequiredService<IBus>());
            var id = Guid.NewGuid();
            await orchestrator.EnqueueMigrationAsync(id, default);

            var completed = await Task.WhenAny(received.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            completed.Should().Be(received.Tcs.Task, "consumer should receive StartMigration");
            (await received.Tcs.Task).Should().Be(id);
        }
        finally { await bus.StopAsync(); }
    }

    public sealed class DlqState { public int Attempts; }

    public sealed class PoisonConsumer(DlqState state) : IConsumer<StartMigration>
    {
        public Task Consume(ConsumeContext<StartMigration> ctx)
        {
            Interlocked.Increment(ref state.Attempts);
            throw new InvalidOperationException("poison");
        }
    }

    public sealed class FaultConsumer(TaskCompletionSource faulted) : IConsumer<Fault<StartMigration>>
    {
        public Task Consume(ConsumeContext<Fault<StartMigration>> ctx)
        {
            faulted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Poison_message_faults_after_configured_retries()
    {
        var state = new DlqState();
        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(faulted);
        services.AddMassTransit(x =>
        {
            x.AddConsumer<PoisonConsumer>(c => c.UseMessageRetry(r => r.Immediate(3)));
            x.AddConsumer<FaultConsumer>();
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(_rabbit.ConnectionString));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        await using var sp = services.BuildServiceProvider(true);
        var bus = sp.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            await sp.GetRequiredService<IPublishEndpoint>().Publish(new StartMigration(Guid.NewGuid()));
            var completed = await Task.WhenAny(faulted.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            completed.Should().Be(faulted.Task, "after retries the message faults (DLQ path)");
            state.Attempts.Should().BeGreaterThanOrEqualTo(4, "1 initial + 3 retries before fault");
        }
        finally { await bus.StopAsync(); }
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~MassTransitOrchestratorTests` → FAILS to compile: `MassTransitJobOrchestrator` does not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Messaging/MassTransitJobOrchestrator.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Infrastructure.Messaging;

/// <summary>
/// MassTransit-backed orchestrator. Enqueue publishes StartMigration; pause/resume/cancel publish
/// their control contracts. Workers consume these (Plan 07). Kept behind IJobOrchestrator so the
/// transport (RabbitMQ today) is swappable.
/// </summary>
public sealed class MassTransitJobOrchestrator : IJobOrchestrator
{
    private readonly IPublishEndpoint _publish;
    private readonly IBus _bus;

    public MassTransitJobOrchestrator(IPublishEndpoint publish, IBus bus)
    {
        _publish = publish;
        _bus = bus;
    }

    public Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct) =>
        _publish.Publish(new StartMigration(mailboxMigrationId), ct);

    public Task RequestPauseAsync(Guid jobId, CancellationToken ct) =>
        _publish.Publish(new PauseJob(jobId), ct);

    public Task RequestResumeAsync(Guid jobId, CancellationToken ct) =>
        _publish.Publish(new ResumeJob(jobId), ct);

    public Task RequestCancelAsync(Guid jobId, CancellationToken ct) =>
        _publish.Publish(new CancelJob(jobId), ct);
}

// Local control contracts (not in CONTRACTS.md §4 message list, which covers data-plane messages).
// These are infra-internal control signals consumed by Workers.
public sealed record PauseJob(Guid JobId);
public sealed record ResumeJob(Guid JobId);
public sealed record CancelJob(Guid JobId);
```

> Note: `StartMigration`, `MigrateFolder`, `MigrateBatch`, `MigrationProgressEvent`, `NeedsDecisionEvent` live in `EMaigrator.Core.Contracts` (CONTRACTS.md §4) and are used verbatim. The three control records (`PauseJob`/`ResumeJob`/`CancelJob`) are not defined in CONTRACTS and are introduced here as infra-local signals; if Plan 07/08 needs them in the shared contract, that is a coordination event (update CONTRACTS first).

Create `src/EMaigrator.Infrastructure/Messaging/MassTransitConfig.cs`:

```csharp
using EMaigrator.Core.Configuration;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Messaging;

public static class MassTransitConfig
{
    /// <summary>
    /// Registers MassTransit over RabbitMQ with prefetch/concurrency and a redelivery+DLQ policy
    /// from OrchestrationOptions. Consumer registration (workers) is supplied by the caller.
    /// </summary>
    public static IServiceCollection AddEmaigratorMessaging(
        this IServiceCollection services,
        string rabbitConnectionString,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddMassTransit(x =>
        {
            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var orch = ctx.GetRequiredService<IOptions<OrchestrationOptions>>().Value;
                cfg.Host(new Uri(rabbitConnectionString));
                cfg.PrefetchCount = (ushort)orch.ConsumerPrefetch;
                cfg.ConcurrentMessageLimit = orch.ConsumerPrefetch;

                cfg.UseMessageRetry(r => r.Immediate(orch.DlqRetryCount));
                cfg.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)));

                cfg.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }
}
```

Wire into `DependencyInjection.cs`:

```csharp
        var orchSection = config.GetSection($"{InfrastructureOptions.SectionName}:Orchestration");
        services.Configure<EMaigrator.Core.Configuration.OrchestrationOptions>(orchSection);
        services.AddEmaigratorMessaging(
            config.GetSection(InfrastructureOptions.SectionName)["RabbitMqConnectionString"] ?? "");
        services.AddScoped<EMaigrator.Core.Abstractions.IJobOrchestrator, Messaging.MassTransitJobOrchestrator>();
```

(`AddEmaigratorMessaging` is invoked with no consumer registration here; Plan 07 Workers pass their consumers via the `configureConsumers` overload in their own host.)

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~MassTransitOrchestratorTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Messaging src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/Messaging src/EMaigrator.Infrastructure.Tests/Fixtures/RabbitMqFixture.cs
git commit -m "feat(infra): wire MassTransit/RabbitMQ with DLQ and IJobOrchestrator

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: OpenTelemetry + Serilog wiring with a credential-scrubbing enricher

**Goal:** Add `AddEmaigratorObservability` (OpenTelemetry traces/metrics + Serilog→OTLP logs) and a Serilog enricher/sink-filter that scrubs known-secret property names, proving via a captured in-memory Serilog sink that a log event carrying a credential value/property never emits the plaintext.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Observability/ObservabilityExtensions.cs`
- Create: `src/EMaigrator.Infrastructure/Observability/SecretScrubbingEnricher.cs`
- Create: `src/EMaigrator.Infrastructure/Observability/Telemetry.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Observability/SecretScrubbingTests.cs`

**Acceptance Criteria:**
- [ ] `Telemetry` exposes the `ActivitySource` and `Meter` names (`"EMaigrator"`) plus counters for messages-migrated, 429-hits, DLQ-growth (instrument creation only; emission lives in Workers).
- [ ] `SecretScrubbingEnricher` redacts any log property whose name matches a secret-name pattern (`password`, `secret`, `token`, `apikey`, `clientsecret`, `cipherblob`, `credential`, `authorization`) to `"***REDACTED***"`.
- [ ] `AddEmaigratorObservability(IServiceCollection, IConfiguration)` registers OTel tracing+metrics with OTLP exporter and configures Serilog with the scrubbing enricher.
- [ ] A test writes a Serilog event with a `password` property and a message-template token holding a secret value into a captured sink and asserts the rendered output and properties contain `***REDACTED***` and not the plaintext.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~SecretScrubbingTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Observability/SecretScrubbingTests.cs`:

```csharp
using EMaigrator.Infrastructure.Observability;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.InMemory;
using Serilog.Sinks.InMemory.Assertions;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Observability;

public class SecretScrubbingTests
{
    [Fact]
    public void Secret_named_properties_are_redacted()
    {
        const string plaintext = "Sup3rSecretPassw0rd!";
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("connecting with {Password} and {ClientSecret}", plaintext, "client-xyz");

        var evt = sink.LogEvents.Single();
        var rendered = evt.RenderMessage();
        rendered.Should().NotContain(plaintext);
        rendered.Should().NotContain("client-xyz");
        ((ScalarValue)evt.Properties["Password"]).Value.Should().Be("***REDACTED***");
        ((ScalarValue)evt.Properties["ClientSecret"]).Value.Should().Be("***REDACTED***");
    }

    [Fact]
    public void Nonsecret_properties_pass_through()
    {
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("migrating folder {SourceFolder}", "INBOX");

        var evt = sink.LogEvents.Single();
        ((ScalarValue)evt.Properties["SourceFolder"]).Value.Should().Be("INBOX");
    }
}
```

Add the in-memory Serilog sink to the test project `EMaigrator.Infrastructure.Tests.csproj`:

```xml
    <PackageReference Include="Serilog.Sinks.InMemory" Version="0.11.0" />
    <PackageReference Include="Serilog.Sinks.InMemory.Assertions" Version="0.11.0" />
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~SecretScrubbingTests` → FAILS to compile: `SecretScrubbingEnricher` does not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Observability/SecretScrubbingEnricher.cs`:

```csharp
using Serilog.Core;
using Serilog.Events;

namespace EMaigrator.Infrastructure.Observability;

/// <summary>
/// Redacts log properties whose names indicate secrets. Defense-in-depth: secrets should never be
/// logged in the first place, but this guarantees zero plaintext credentials reach any sink.
/// </summary>
public sealed class SecretScrubbingEnricher : ILogEventEnricher
{
    private const string Redacted = "***REDACTED***";

    private static readonly string[] SecretMarkers =
    {
        "password", "secret", "token", "apikey", "api_key", "clientsecret",
        "client_secret", "cipherblob", "credential", "authorization", "sajson", "privatekey"
    };

    private static bool IsSecretName(string name)
    {
        foreach (var m in SecretMarkers)
            if (name.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var prop in logEvent.Properties.ToArray())
        {
            if (IsSecretName(prop.Key))
            {
                logEvent.AddOrUpdateProperty(
                    propertyFactory.CreateProperty(prop.Key, Redacted));
            }
        }
    }
}
```

Create `src/EMaigrator.Infrastructure/Observability/Telemetry.cs`:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EMaigrator.Infrastructure.Observability;

/// <summary>Shared OpenTelemetry instruments. Emission happens in Workers; this owns the names/handles.</summary>
public static class Telemetry
{
    public const string SourceName = "EMaigrator";
    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> MessagesMigrated =
        Meter.CreateCounter<long>("emaigrator.messages.migrated");
    public static readonly Counter<long> RateLimitHits =
        Meter.CreateCounter<long>("emaigrator.ratelimit.429");
    public static readonly Counter<long> DlqMessages =
        Meter.CreateCounter<long>("emaigrator.dlq.parked");
}
```

Create `src/EMaigrator.Infrastructure/Observability/ObservabilityExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace EMaigrator.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Registers OpenTelemetry traces + metrics (OTLP exporter) and a Serilog logger with the
    /// secret-scrubbing enricher and OTLP log sink. OTLP endpoint comes from OTEL_EXPORTER_OTLP_ENDPOINT.
    /// </summary>
    public static IServiceCollection AddEmaigratorObservability(this IServiceCollection services, IConfiguration config)
    {
        var serviceName = config["OTEL_SERVICE_NAME"] ?? "emaigrator";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(Telemetry.SourceName)
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddMeter(Telemetry.SourceName)
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        Log.Logger = BuildLogger(config, serviceName);
        services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: false));
        return services;
    }

    /// <summary>Builds the scrubbing Serilog logger; exposed so tests and hosts share one config.</summary>
    public static Serilog.ILogger BuildLogger(IConfiguration config, string serviceName)
    {
        var cfg = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .Enrich.WithProperty("service.name", serviceName)
            .MinimumLevel.Information()
            .WriteTo.Console();

        var otlp = config["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            cfg = cfg.WriteTo.OpenTelemetry(o =>
            {
                o.Endpoint = otlp;
                o.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
            });
        }
        return cfg.CreateLogger();
    }
}
```

Wire into `DependencyInjection.cs`:

```csharp
        services.AddEmaigratorObservability(config);
```

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~SecretScrubbingTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Observability src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/Observability
git commit -m "feat(infra): add OTel/Serilog wiring with secret-scrubbing enricher

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: ASP.NET health checks for Postgres/RabbitMQ/Redis

**Goal:** Add `AddEmaigratorHealthChecks` registering Postgres, RabbitMQ, and Redis health checks tagged for liveness/readiness, proving against the three Testcontainers that the aggregate report is `Healthy` and each named check is present.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Health/HealthCheckExtensions.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Health/HealthCheckTests.cs`

**Acceptance Criteria:**
- [ ] `AddEmaigratorHealthChecks(IServiceCollection, InfrastructureOptions)` registers checks named `postgres`, `rabbitmq`, `redis` tagged `"ready"`.
- [ ] An integration test wiring all three Testcontainers runs `HealthCheckService.CheckHealthAsync()` and asserts overall status `Healthy` and that all three named entries exist.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~HealthCheckTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Health/HealthCheckTests.cs`:

```csharp
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Health;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Health;

[Collection("infra-trio")]
public class HealthCheckTests
{
    private readonly InfraTrioFixture _trio;
    public HealthCheckTests(InfraTrioFixture trio) => _trio = trio;

    [Fact]
    public async Task All_dependencies_report_healthy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmaigratorHealthChecks(new InfrastructureOptions
        {
            PostgresConnectionString = _trio.Postgres.ConnectionString,
            RabbitMqConnectionString = _trio.Rabbit.ConnectionString,
            RedisConnectionString = _trio.Redis.ConnectionString,
        });
        await using var sp = services.BuildServiceProvider();

        var svc = sp.GetRequiredService<HealthCheckService>();
        var report = await svc.CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Keys.Should().Contain(new[] { "postgres", "rabbitmq", "redis" });
    }
}
```

Create the combined trio fixture `src/EMaigrator.Infrastructure.Tests/Fixtures/InfraTrioFixture.cs`:

```csharp
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Fixtures;

public sealed class InfraTrioFixture : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();
    public RedisFixture Redis { get; } = new();
    public RabbitMqFixture Rabbit { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.InitializeAsync(), Redis.InitializeAsync(), Rabbit.InitializeAsync());
    }
    public async Task DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync(), Redis.DisposeAsync(), Rabbit.DisposeAsync());
    }
}

[CollectionDefinition("infra-trio")]
public sealed class InfraTrioCollection : ICollectionFixture<InfraTrioFixture> { }
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~HealthCheckTests` → FAILS to compile: `AddEmaigratorHealthChecks` does not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Health/HealthCheckExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Infrastructure.Health;

public static class HealthCheckExtensions
{
    /// <summary>Registers readiness health checks for Postgres, RabbitMQ, and Redis.</summary>
    public static IServiceCollection AddEmaigratorHealthChecks(this IServiceCollection services, InfrastructureOptions options)
    {
        services.AddHealthChecks()
            .AddNpgSql(options.PostgresConnectionString, name: "postgres", tags: new[] { "ready" })
            .AddRabbitMQ(
                rabbitConnectionString: options.RabbitMqConnectionString,
                name: "rabbitmq", tags: new[] { "ready" })
            .AddRedis(options.RedisConnectionString, name: "redis", tags: new[] { "ready" });
        return services;
    }
}
```

Wire into `DependencyInjection.cs` (resolve the bound options to register checks):

```csharp
        var infraOptions = config.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>() ?? new();
        services.AddEmaigratorHealthChecks(infraOptions);
```

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~HealthCheckTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Health src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/Health src/EMaigrator.Infrastructure.Tests/Fixtures/InfraTrioFixture.cs
git commit -m "feat(infra): add health checks for Postgres, RabbitMQ, Redis

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: 30-day MigrationLog purge + credential-purge-on-terminal hooks

**Goal:** Implement `LogRetentionPurgeService` (a `BackgroundService` that deletes `MigrationLogRow` older than `RetentionOptions.LogRetentionDays`) and `CredentialPurgeHook` (deletes all `CredentialRow` and `ISecretStore` entries for a job when it reaches a terminal state), proving both against Testcontainers Postgres.

**Files:**
- Create: `src/EMaigrator.Infrastructure/Retention/LogRetentionPurgeService.cs`
- Create: `src/EMaigrator.Infrastructure/Retention/ICredentialPurgeHook.cs`
- Create: `src/EMaigrator.Infrastructure/Retention/CredentialPurgeHook.cs`
- Modify: `src/EMaigrator.Infrastructure/DependencyInjection.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Retention/LogRetentionPurgeTests.cs`
- Create: `src/EMaigrator.Infrastructure.Tests/Retention/CredentialPurgeHookTests.cs`

**Acceptance Criteria:**
- [ ] `LogRetentionPurgeService.PurgeOnceAsync(now, ct)` deletes log rows with `CreatedAt < now - LogRetentionDays` and returns the deleted count; rows newer than the cutoff survive.
- [ ] `CredentialPurgeHook.PurgeForJobAsync(jobId, ct)` deletes every `CredentialRow` referenced by the job's connection refs (and calls `ISecretStore.PurgeAsync` for each `SecretRef`); after it runs, no credential row for that job remains.
- [ ] Terminal-state set is `Completed`, `Partial`, `Failed`, `Cancelled` (matches `JobStatus`); the hook is a no-op for non-terminal status.
- [ ] Both behaviors proven against Testcontainers Postgres.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter "FullyQualifiedName~LogRetentionPurgeTests|FullyQualifiedName~CredentialPurgeHookTests"` → all pass.

**Steps:**

1. - [ ] **Write the failing tests.** Create `src/EMaigrator.Infrastructure.Tests/Retention/LogRetentionPurgeTests.cs`:

```csharp
using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Retention;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Retention;

[Collection("postgres")]
public class LogRetentionPurgeTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    public LogRetentionPurgeTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> o) : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(o);
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Purges_only_logs_older_than_retention()
    {
        var now = DateTimeOffset.UtcNow;
        var mig = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.MigrationLogs.Add(new MigrationLogRow { MailboxMigrationId = mig, SourceFolder = "f", DestFolder = "f", Status = "Migrated", CreatedAt = now.AddDays(-31) });
            ctx.MigrationLogs.Add(new MigrationLogRow { MailboxMigrationId = mig, SourceFolder = "f", DestFolder = "f", Status = "Migrated", CreatedAt = now.AddDays(-29) });
            await ctx.SaveChangesAsync();
        }

        var svc = new LogRetentionPurgeService(new Factory(DbOptions),
            Options.Create(new RetentionOptions { LogRetentionDays = 30 }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LogRetentionPurgeService>.Instance);

        var deleted = await svc.PurgeOnceAsync(now, default);

        deleted.Should().Be(1);
        await using var verify = new EmaigratorDbContext(DbOptions);
        (await verify.MigrationLogs.CountAsync(r => r.MailboxMigrationId == mig)).Should().Be(1);
    }
}
```

Create `src/EMaigrator.Infrastructure.Tests/Retention/CredentialPurgeHookTests.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Retention;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Retention;

[Collection("postgres")]
public class CredentialPurgeHookTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    public CredentialPurgeHookTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> o) : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(o);
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Purges_credentials_when_job_terminal()
    {
        var tenant = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.Jobs.Add(new Job { Id = jobId, TenantId = tenant, SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"), SourceConnectionRef = "cred:src", DestConnectionRef = "cred:dst",
                Status = JobStatus.Completed, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            ctx.Credentials.Add(new CredentialRow { Id = Guid.NewGuid(), TenantId = tenant, SecretRef = "cred:src", CipherBlob = "x", CreatedAt = DateTimeOffset.UtcNow });
            ctx.Credentials.Add(new CredentialRow { Id = Guid.NewGuid(), TenantId = tenant, SecretRef = "cred:dst", CipherBlob = "y", CreatedAt = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync();
        }

        var secretStore = Substitute.For<ISecretStore>();
        var hook = new CredentialPurgeHook(new Factory(DbOptions), secretStore);

        await hook.PurgeForJobAsync(jobId, default);

        await using var verify = new EmaigratorDbContext(DbOptions);
        (await verify.Credentials.AnyAsync(c => c.TenantId == tenant)).Should().BeFalse();
        await secretStore.Received(1).PurgeAsync("cred:src", Arg.Any<CancellationToken>());
        await secretStore.Received(1).PurgeAsync("cred:dst", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Noop_when_job_not_terminal()
    {
        var jobId = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.Jobs.Add(new Job { Id = jobId, TenantId = Guid.NewGuid(), SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"), SourceConnectionRef = "cred:a", Status = JobStatus.Running,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            ctx.Credentials.Add(new CredentialRow { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SecretRef = "cred:a", CipherBlob = "x", CreatedAt = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync();
        }

        var hook = new CredentialPurgeHook(new Factory(DbOptions), Substitute.For<ISecretStore>());
        await hook.PurgeForJobAsync(jobId, default);

        await using var verify = new EmaigratorDbContext(DbOptions);
        (await verify.Credentials.AnyAsync(c => c.SecretRef == "cred:a")).Should().BeTrue("running job keeps creds");
    }
}
```

Add NSubstitute to the test project if not present (`EMaigrator.Infrastructure.Tests.csproj`):

```xml
    <PackageReference Include="NSubstitute" Version="5.3.0" />
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter "FullyQualifiedName~LogRetentionPurgeTests|FullyQualifiedName~CredentialPurgeHookTests"` → FAILS to compile: types do not exist.

3. - [ ] **Minimal implementation.** Create `src/EMaigrator.Infrastructure/Retention/LogRetentionPurgeService.cs`:

```csharp
using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Retention;

/// <summary>Deletes MigrationLog rows older than the configured retention window (default 30 days).</summary>
public sealed class LogRetentionPurgeService : BackgroundService
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;
    private readonly RetentionOptions _options;
    private readonly ILogger<LogRetentionPurgeService> _logger;

    public LogRetentionPurgeService(IDbContextFactory<EmaigratorDbContext> factory,
        IOptions<RetentionOptions> options, ILogger<LogRetentionPurgeService> logger)
    {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> PurgeOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-_options.LogRetentionDays);
        await using var ctx = _factory.CreateDbContext();
        var deleted = await ctx.MigrationLogs.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0)
            _logger.LogInformation("Purged {Count} migration log rows older than {Cutoff}", deleted, cutoff);
        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try { await PurgeOnceAsync(DateTimeOffset.UtcNow, stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Log retention purge failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

Create `src/EMaigrator.Infrastructure/Retention/ICredentialPurgeHook.cs`:

```csharp
namespace EMaigrator.Infrastructure.Retention;

/// <summary>Purges all stored credentials for a job once it reaches a terminal state (DESIGN.md §10).</summary>
public interface ICredentialPurgeHook
{
    Task PurgeForJobAsync(Guid jobId, CancellationToken ct);
}
```

Create `src/EMaigrator.Infrastructure/Retention/CredentialPurgeHook.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Retention;

public sealed class CredentialPurgeHook : ICredentialPurgeHook
{
    private static readonly JobStatus[] Terminal =
        { JobStatus.Completed, JobStatus.Partial, JobStatus.Failed, JobStatus.Cancelled };

    private readonly IDbContextFactory<EmaigratorDbContext> _factory;
    private readonly ISecretStore _secretStore;

    public CredentialPurgeHook(IDbContextFactory<EmaigratorDbContext> factory, ISecretStore secretStore)
    {
        _factory = factory;
        _secretStore = secretStore;
    }

    public async Task PurgeForJobAsync(Guid jobId, CancellationToken ct)
    {
        await using var ctx = _factory.CreateDbContext();
        var job = await ctx.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !Terminal.Contains(job.Status)) return;

        var refs = new[] { job.SourceConnectionRef, job.DestConnectionRef }
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .Distinct()
            .ToArray();

        foreach (var secretRef in refs)
        {
            await _secretStore.PurgeAsync(secretRef, ct);
        }

        await ctx.Credentials.Where(c => refs.Contains(c.SecretRef)).ExecuteDeleteAsync(ct);
    }
}
```

Wire into `DependencyInjection.cs`:

```csharp
        var retentionSection = config.GetSection($"{InfrastructureOptions.SectionName}:Retention");
        services.Configure<EMaigrator.Core.Configuration.RetentionOptions>(retentionSection);
        services.AddSingleton<Retention.ICredentialPurgeHook, Retention.CredentialPurgeHook>();
        services.AddHostedService<Retention.LogRetentionPurgeService>();
```

> Note: the purge hook is invoked by Workers (Plan 07) when a job transitions to terminal; this plan provides and tests the hook itself.

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter "FullyQualifiedName~LogRetentionPurgeTests|FullyQualifiedName~CredentialPurgeHookTests"` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/Retention src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/Retention
git commit -m "feat(infra): add 30-day log purge and credential-purge-on-terminal hook

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: Functional Verification — full AddInfrastructure composition against the real trio

**Goal:** Prove the subsystem's headline behavior end-to-end: a host built solely from `AddInfrastructure` (pointed at live Postgres+Redis+RabbitMQ Testcontainers) resolves and exercises all four Core abstractions (`ILedger`, `ISecretStore`, `IRateLimiter`, `IJobOrchestrator`) plus health checks in one composed pipeline.

**Files:**
- Create: `src/EMaigrator.Infrastructure.Tests/AddInfrastructureEndToEndTests.cs`

**Acceptance Criteria:**
- [ ] A `ServiceCollection` configured only via `AddInfrastructure` (with the trio connection strings + `SecretStore:Mode=LocalKey`) resolves `ILedger`, `ISecretStore`, `IRateLimiter`, `IJobOrchestrator`, `HealthCheckService`.
- [ ] After `MigrateAsync`, the resolved `ILedger` marks and reads back a key; `ISecretStore` round-trips a secret (ciphertext at rest); `IRateLimiter` grants then throttles; `IJobOrchestrator.EnqueueMigrationAsync` publishes `StartMigration` consumed by a registered test consumer.
- [ ] `HealthCheckService.CheckHealthAsync()` returns `Healthy`.

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~AddInfrastructureEndToEndTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/AddInfrastructureEndToEndTests.cs`:

```csharp
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Security.Cryptography;
using Xunit;

namespace EMaigrator.Infrastructure.Tests;

[Collection("infra-trio")]
public class AddInfrastructureEndToEndTests
{
    private readonly InfraTrioFixture _trio;
    public AddInfrastructureEndToEndTests(InfraTrioFixture trio) => _trio = trio;

    public sealed class CaptureConsumer : IConsumer<StartMigration>
    {
        public static readonly TaskCompletionSource<Guid> Seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Consume(ConsumeContext<StartMigration> ctx) { Seen.TrySetResult(ctx.Message.MailboxMigrationId); return Task.CompletedTask; }
    }

    private IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Infrastructure:PostgresConnectionString"] = _trio.Postgres.ConnectionString,
        ["Infrastructure:RedisConnectionString"] = _trio.Redis.ConnectionString,
        ["Infrastructure:RabbitMqConnectionString"] = _trio.Rabbit.ConnectionString,
        ["Infrastructure:SecretStore:Mode"] = "LocalKey",
        ["Infrastructure:SecretStore:KeyRef"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Infrastructure:RateLimit:Buckets:default:RefillPerSecond"] = "0.001",
        ["Infrastructure:RateLimit:Buckets:default:Burst"] = "1",
    }).Build();

    [Fact]
    public async Task Composed_infrastructure_exercises_all_seams()
    {
        var services = new ServiceCollection();
        // registerBus: false so the test owns the single bus registration (with its capture consumer);
        // AddInfrastructure still registers ledger/secrets/rate-limiter/orchestrator/health.
        services.AddInfrastructure(Config(), registerBus: false);
        services.AddMassTransit(x =>
        {
            x.AddConsumer<CaptureConsumer>();
            x.UsingRabbitMq((ctx, cfg) => { cfg.Host(new Uri(_trio.Rabbit.ConnectionString)); cfg.ConfigureEndpoints(ctx); });
        });
        await using var sp = services.BuildServiceProvider(true);

        // migrate schema
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctxFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
            await using var ctx = ctxFactory.CreateDbContext();
            await ctx.Database.MigrateAsync();
        }

        var bus = sp.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            using var scope = sp.CreateScope();
            var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
            var limiter = sp.GetRequiredService<IRateLimiter>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IJobOrchestrator>();
            var health = sp.GetRequiredService<HealthCheckService>();

            var mig = Guid.NewGuid();
            await ledger.MarkAsync(mig, "k", "INBOX", "Inbox", LedgerStatus.Migrated, null, default);
            (await ledger.IsDoneAsync(mig, "k", default)).Should().BeTrue();

            var secretRef = await secrets.StoreAsync(Guid.NewGuid().ToString(), "secret", default);
            (await secrets.RetrieveAsync(secretRef, default)).Should().Be("secret");

            var key = new RateLimitKey(new ProviderId("graph"), Guid.NewGuid().ToString("N"));
            (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
            (await limiter.TryAcquireAsync(key, 1, default)).Should().BeFalse();

            await orchestrator.EnqueueMigrationAsync(mig, default);
            var done = await Task.WhenAny(CaptureConsumer.Seen.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            done.Should().Be(CaptureConsumer.Seen.Task);

            (await health.CheckHealthAsync()).Status.Should().Be(HealthStatus.Healthy);
        }
        finally { await bus.StopAsync(); }
    }
}
```

2. - [ ] **Run it — expect FAIL.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~AddInfrastructureEndToEndTests` → FAILS to compile: `AddInfrastructure` has no `registerBus` parameter (the overload added in this step does not exist yet).

3. - [ ] **Minimal implementation.** Add a `registerBus` parameter to `AddInfrastructure` so a single bus can be registered by the host/test (avoiding a second conflicting `IBusControl`). Because Task 9 already calls `AddEmaigratorMessaging` unconditionally inside `AddInfrastructure`, gate that one call behind the flag. The body below shows the complete `AddInfrastructure` after all prior tasks have contributed their registrations; the only change in this task is the `registerBus` parameter and the `if (registerBus)` guard around the messaging call. Edit `src/EMaigrator.Infrastructure/DependencyInjection.cs`:

```csharp
using EMaigrator.Infrastructure.Health;
using EMaigrator.Infrastructure.Messaging;
using EMaigrator.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Composes the EMaigrator infrastructure adapters (persistence, secrets, rate limiter,
    /// orchestrator, observability, health checks, retention jobs) behind the Core abstractions.
    /// When <paramref name="registerBus"/> is false, the caller (host/test) owns the single
    /// MassTransit bus registration (so it can attach worker consumers); the orchestrator is still registered.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool registerBus = true)
    {
        // Task 1: options binding
        services.AddOptions<InfrastructureOptions>()
            .Bind(config.GetSection(InfrastructureOptions.SectionName))
            .ValidateOnStart();

        // Task 4: DbContext factory + ledger
        services.AddDbContextFactory<Data.EmaigratorDbContext>((sp, b) =>
        {
            var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
            b.UseNpgsql(opts.PostgresConnectionString,
                npg => npg.MigrationsAssembly("EMaigrator.Infrastructure"));
        });
        services.AddScoped<EMaigrator.Core.Abstractions.ILedger, Persistence.PostgresLedger>();

        // Task 5/6: envelope secret store (mode-switched key wrapper)
        services.AddSingleton<Secrets.EnvelopeCipher>();
        var ssMode = config.GetSection(InfrastructureOptions.SectionName)["SecretStore:Mode"] ?? "LocalKey";
        if (ssMode == "AzureKeyVault")
        {
            services.AddSingleton<Secrets.IKmsClient, Secrets.AzureKeyVaultKmsClient>();
            services.AddSingleton<Secrets.KmsKeyWrapper>();
        }
        services.AddSingleton<EMaigrator.Core.Abstractions.ISecretStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
            var factory = sp.GetRequiredService<IDbContextFactory<Data.EmaigratorDbContext>>();
            var cipher = sp.GetRequiredService<Secrets.EnvelopeCipher>();
            var ssOptions = Options.Create(opts.SecretStore);
            Secrets.IKeyWrapper wrapper = opts.SecretStore.Mode switch
            {
                "AzureKeyVault" => sp.GetRequiredService<Secrets.KmsKeyWrapper>(),
                _ => new Secrets.LocalKeyWrapper(ssOptions),
            };
            return new Secrets.LocalKeyEnvelopeSecretStore(factory, wrapper, cipher);
        });

        // Task 7/8: Redis rate limiter
        services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
            return StackExchange.Redis.ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        });
        services.Configure<EMaigrator.Core.Configuration.RateLimitOptions>(o =>
        {
            var opts = config.GetSection(InfrastructureOptions.SectionName)
                .Get<InfrastructureOptions>() ?? new InfrastructureOptions();
            o.Buckets = opts.RateLimit.Buckets;
        });
        services.AddSingleton<EMaigrator.Core.Abstractions.IRateLimiter, RateLimiting.RedisRateLimiter>();

        // Task 9: MassTransit/RabbitMQ (only when this method owns bus registration)
        var orchSection = config.GetSection($"{InfrastructureOptions.SectionName}:Orchestration");
        services.Configure<EMaigrator.Core.Configuration.OrchestrationOptions>(orchSection);
        if (registerBus)
        {
            services.AddEmaigratorMessaging(
                config.GetSection(InfrastructureOptions.SectionName)["RabbitMqConnectionString"] ?? "");
        }
        services.AddScoped<EMaigrator.Core.Abstractions.IJobOrchestrator, Messaging.MassTransitJobOrchestrator>();

        // Task 10: observability
        services.AddEmaigratorObservability(config);

        // Task 11: health checks
        var infraOptions = config.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>() ?? new();
        services.AddEmaigratorHealthChecks(infraOptions);

        // Task 12: retention + credential purge
        var retentionSection = config.GetSection($"{InfrastructureOptions.SectionName}:Retention");
        services.Configure<EMaigrator.Core.Configuration.RetentionOptions>(retentionSection);
        services.AddSingleton<Retention.ICredentialPurgeHook, Retention.CredentialPurgeHook>();
        services.AddHostedService<Retention.LogRetentionPurgeService>();

        return services;
    }
}
```

The test (Step 1) already calls `services.AddInfrastructure(Config(), registerBus: false)` and registers its own consumer-bearing bus, so there is exactly one `IBusControl`.

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~AddInfrastructureEndToEndTests` → all pass.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure/DependencyInjection.cs src/EMaigrator.Infrastructure.Tests/AddInfrastructureEndToEndTests.cs
git commit -m "test(infra): functional E2E composing AddInfrastructure against live trio

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 14: Security Verification — credentials ciphertext, no-PII tables, scrubbed logs, terminal purge

**Goal:** Prove the plan's security focus with independent, output-capturing assertions against real infrastructure.

**USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Infrastructure.Tests/Security/InfrastructureSecurityTests.cs`

**Acceptance Criteria:**
- [ ] Raw-SQL read of `credentials.CipherBlob` (via Npgsql, not the store) returns a value that does NOT contain the plaintext canary and does NOT base64-decode to anything containing the canary — captured in the assertion message.
- [ ] A schema query against `information_schema.columns` proves `ledger_entries` and `migration_logs` have NO column whose name contains body/attachment/content/raw/mime, and `migration_logs` has NO column containing sender/recipient/from/to/cc/bcc/address.
- [ ] A captured Serilog in-memory sink, fed an event containing a credential value and a `password`/`clientSecret`/`cipherBlob` property, shows ZERO plaintext credential occurrences across all rendered messages and property values.
- [ ] Credential purge on terminal state: store a secret, attach it to a `Completed` job, run `ICredentialPurgeHook.PurgeForJobAsync`, then a raw-SQL count of `credentials` for that secretRef is `0` and `ISecretStore.RetrieveAsync` throws.
- [ ] 30-day log retention: insert one `migration_logs` row older than `LogRetentionDays` and one newer, run `LogRetentionPurgeService.PurgeOnceAsync`, then a raw-SQL count proves the over-retention row is gone and the recent row survives (the security-relevant "logs do not linger past 30 days" guarantee).

**Verify:** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~InfrastructureSecurityTests` → all pass.

**Steps:**

1. - [ ] **Write the failing test.** Create `src/EMaigrator.Infrastructure.Tests/Security/InfrastructureSecurityTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Observability;
using EMaigrator.Infrastructure.Retention;
using EMaigrator.Infrastructure.Secrets;
using EMaigrator.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using Serilog.Sinks.InMemory;
using Xunit;

namespace EMaigrator.Infrastructure.Tests.Security;

[Collection("postgres")]
public class InfrastructureSecurityTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    public InfrastructureSecurityTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> o) : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(o);
    }

    private LocalKeyEnvelopeSecretStore NewStore()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var opts = Options.Create(new SecretStoreOptions { Mode = "LocalKey", KeyRef = key });
        return new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new LocalKeyWrapper(opts), new EnvelopeCipher());
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Credential_blob_is_ciphertext_via_raw_sql()
    {
        const string canary = "PLAINTEXT-CANARY-c0ffee";
        var store = NewStore();
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), canary, default);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT ""CipherBlob"" FROM credentials WHERE ""SecretRef"" = @r", conn);
        cmd.Parameters.AddWithValue("r", secretRef);
        var blob = (string)(await cmd.ExecuteScalarAsync())!;

        blob.Should().NotContain(canary, $"raw DB value must be ciphertext: {blob}");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(blob));
        decoded.Should().NotContain(canary, "base64-decoded blob must not reveal plaintext");
    }

    [Theory]
    [InlineData("ledger_entries", new[] { "body", "attachment", "content", "raw", "mime" })]
    [InlineData("migration_logs", new[] { "body", "attachment", "content", "raw", "mime", "sender", "recipient", "from", "to", "cc", "bcc", "address" })]
    public async Task Metadata_tables_have_no_forbidden_columns(string table, string[] forbidden)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = @t", conn);
        cmd.Parameters.AddWithValue("t", table);
        var cols = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) cols.Add(reader.GetString(0));

        var offending = cols.Where(c => forbidden.Any(f => c.Contains(f, StringComparison.OrdinalIgnoreCase))).ToArray();
        offending.Should().BeEmpty($"{table} columns: [{string.Join(", ", cols)}]");
    }

    [Fact]
    public void Logs_contain_zero_plaintext_credentials()
    {
        const string canary = "LOG-CANARY-s3cr3t";
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("auth {Password} {ClientSecret} {CipherBlob} for {SourceFolder}", canary, canary, canary, "INBOX");

        var allText = string.Join("\n", sink.LogEvents.Select(e =>
            e.RenderMessage() + "|" + string.Join(",", e.Properties.Select(p => p.Value.ToString()))));
        allText.Should().NotContain(canary, $"no plaintext credential may appear in logs. Captured: {allText}");
    }

    [Fact]
    public async Task Credentials_purged_on_terminal_state()
    {
        var store = NewStore();
        var tenant = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var secretRef = await store.StoreAsync(tenant.ToString(), "to-be-purged", default);

        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.Jobs.Add(new Job { Id = jobId, TenantId = tenant, SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"), SourceConnectionRef = secretRef,
                Status = JobStatus.Completed, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync();
        }

        var hook = new CredentialPurgeHook(new Factory(DbOptions), store);
        await hook.PurgeForJobAsync(jobId, default);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT COUNT(*) FROM credentials WHERE ""SecretRef"" = @r", conn);
        cmd.Parameters.AddWithValue("r", secretRef);
        ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(0);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RetrieveAsync(secretRef, default));
    }

    [Fact]
    public async Task Logs_do_not_linger_past_retention()
    {
        var now = DateTimeOffset.UtcNow;
        var mig = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.MigrationLogs.Add(new MigrationLogRow { MailboxMigrationId = mig, SourceFolder = "f", DestFolder = "f", Status = "Migrated", CreatedAt = now.AddDays(-31) });
            ctx.MigrationLogs.Add(new MigrationLogRow { MailboxMigrationId = mig, SourceFolder = "f", DestFolder = "f", Status = "Migrated", CreatedAt = now.AddDays(-1) });
            await ctx.SaveChangesAsync();
        }

        var svc = new LogRetentionPurgeService(new Factory(DbOptions),
            Options.Create(new RetentionOptions { LogRetentionDays = 30 }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LogRetentionPurgeService>.Instance);
        var deleted = await svc.PurgeOnceAsync(now, default);
        deleted.Should().Be(1, "exactly the over-retention row must be purged");

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT COUNT(*) FROM migration_logs WHERE ""MailboxMigrationId"" = @m", conn);
        cmd.Parameters.AddWithValue("m", mig);
        ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(1, "only the within-retention row survives");
    }
}
```

2. - [ ] **Run it — expect FAIL on first authoring** if any earlier task's invariant regressed (e.g. a stray column, an unscrubbed property). `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~InfrastructureSecurityTests` → FAILS until all five invariants hold.

3. - [ ] **Make it green.** These assertions are satisfied by Tasks 2/3 (no-PII columns), 5/6 (ciphertext), 10 (scrubbing), 12 (credential purge + 30-day log retention). If any fails, fix the offending implementation (e.g. remove a column from the entity + regenerate the migration; add a missing marker to `SecretScrubbingEnricher.SecretMarkers`). No new production code should be needed if prior tasks are correct.

4. - [ ] **Run it — expect PASS.** `dotnet test src/EMaigrator.Infrastructure.Tests --filter FullyQualifiedName~InfrastructureSecurityTests` → all pass. Capture and retain the test output (ciphertext value, column lists, captured log text) as gate evidence.

5. - [ ] **Commit.**
```
git add src/EMaigrator.Infrastructure.Tests/Security
git commit -m "test(infra): security verification — ciphertext creds, no-PII tables, scrubbed logs, terminal purge

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
