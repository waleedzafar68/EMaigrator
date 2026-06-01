# Repo & Solution Scaffolding Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Scaffold the public monorepo per `DESIGN.md §15` — the `EMaigrator.sln` with all production and test projects wired with references that physically enforce the dependency rule, a `Directory.Build.props` enforcing nullable/langversion/analyzers/warnaserror, an architecture-test that fails if Core ever takes a dependency, a Vite+React 19+TS web stub with Tailwind+shadcn, a `/deploy` docker-compose (Postgres+RabbitMQ+Redis) with Dockerfiles, and CI (build + test with coverage + `dotnet list package --vulnerable` failing on any finding + web build + vitest) — proven by a building solution and a green smoke test.

**Architecture:** A single .NET 10 solution under `/src` with `EMaigrator.Core` referencing nothing, connectors and `EMaigrator.Infrastructure` referencing only `Core`, and `EMaigrator.Workers`/`Api`/`Cli` composing via project references; mirrored `*.Tests` (and `*.IntegrationTests` where I/O is involved). A `/web` Vite SPA is a pure client stub. `/deploy` provides the four-container parity environment. CI gates every push.

**Tech Stack:** C#/.NET 10 (LTS, C# 13, nullable enabled), xUnit + FluentAssertions + NSubstitute, NetArchTest.Rules for the dependency-rule test; Vite + React 19 + TypeScript + Tailwind v4 + shadcn/ui + Vitest + Testing Library; Docker Compose (postgres:17, rabbitmq:4-management, redis:7); GitHub Actions.

---

### Task 1: Solution skeleton with all projects and references (dependency rule wired)

**Goal:** Create `EMaigrator.sln` under `/src` with every production and test project from `DESIGN.md §15`/CONTRACTS §8 and project references that physically enforce the dependency rule.

**Files:**
- Create: `src/EMaigrator.sln`
- Create: `src/EMaigrator.Core/EMaigrator.Core.csproj`
- Create: `src/EMaigrator.Connectors.Imap/EMaigrator.Connectors.Imap.csproj`
- Create: `src/EMaigrator.Connectors.Graph/EMaigrator.Connectors.Graph.csproj`
- Create: `src/EMaigrator.Connectors.Gmail/EMaigrator.Connectors.Gmail.csproj`
- Create: `src/EMaigrator.Infrastructure/EMaigrator.Infrastructure.csproj`
- Create: `src/EMaigrator.Workers/EMaigrator.Workers.csproj`
- Create: `src/EMaigrator.Api/EMaigrator.Api.csproj`
- Create: `src/EMaigrator.Cli/EMaigrator.Cli.csproj`
- Test: `src/EMaigrator.Core.Tests/EMaigrator.Core.Tests.csproj`, `src/EMaigrator.Connectors.Imap.Tests/EMaigrator.Connectors.Imap.Tests.csproj`, `src/EMaigrator.Connectors.Graph.Tests/EMaigrator.Connectors.Graph.Tests.csproj`, `src/EMaigrator.Connectors.Gmail.Tests/EMaigrator.Connectors.Gmail.Tests.csproj`, `src/EMaigrator.Infrastructure.Tests/EMaigrator.Infrastructure.Tests.csproj`, `src/EMaigrator.Infrastructure.IntegrationTests/EMaigrator.Infrastructure.IntegrationTests.csproj`, `src/EMaigrator.Workers.Tests/EMaigrator.Workers.Tests.csproj`, `src/EMaigrator.Workers.IntegrationTests/EMaigrator.Workers.IntegrationTests.csproj`, `src/EMaigrator.Api.Tests/EMaigrator.Api.Tests.csproj`, `src/EMaigrator.Cli.Tests/EMaigrator.Cli.Tests.csproj`

**Acceptance Criteria:**
- [ ] `dotnet sln src/EMaigrator.sln list` lists all 8 production projects and all 10 test projects.
- [ ] `EMaigrator.Core.csproj` has **zero** `<ProjectReference>` elements.
- [ ] `EMaigrator.Connectors.Imap/Graph/Gmail` and `EMaigrator.Infrastructure` each reference **only** `EMaigrator.Core`.
- [ ] `EMaigrator.Workers` references `Core`, `Infrastructure`, and all three connectors; `EMaigrator.Api` references `Core`, `Infrastructure`, `Workers`; `EMaigrator.Cli` references `Core`, `Infrastructure`, `Workers`, all three connectors.
- [ ] No connector references another connector; no connector references `Infrastructure`.
- [ ] `dotnet build src/EMaigrator.sln` succeeds.

**Verify:** `dotnet build src/EMaigrator.sln -c Release` → `Build succeeded` with `0 Warning(s)` and `0 Error(s)`; and `dotnet sln src/EMaigrator.sln list` lists 18 projects.

**Steps:**

1. - [ ] Write the failing test — a shell verification of the structure. Create `src/structure-check.ps1`:
```powershell
# src/structure-check.ps1 — fails (non-zero exit) until solution + reference graph exist.
$ErrorActionPreference = 'Stop'
$slnDir = $PSScriptRoot

function Fail($msg) { Write-Error $msg; exit 1 }

if (-not (Test-Path "$slnDir/EMaigrator.sln")) { Fail "EMaigrator.sln missing" }

$expected = @(
  'EMaigrator.Core','EMaigrator.Connectors.Imap','EMaigrator.Connectors.Graph',
  'EMaigrator.Connectors.Gmail','EMaigrator.Infrastructure','EMaigrator.Workers',
  'EMaigrator.Api','EMaigrator.Cli',
  'EMaigrator.Core.Tests','EMaigrator.Connectors.Imap.Tests','EMaigrator.Connectors.Graph.Tests',
  'EMaigrator.Connectors.Gmail.Tests','EMaigrator.Infrastructure.Tests',
  'EMaigrator.Infrastructure.IntegrationTests','EMaigrator.Workers.Tests',
  'EMaigrator.Workers.IntegrationTests','EMaigrator.Api.Tests','EMaigrator.Cli.Tests'
)
$listed = dotnet sln "$slnDir/EMaigrator.sln" list
foreach ($p in $expected) {
  if (-not ($listed -match [regex]::Escape($p))) { Fail "Project not in solution: $p" }
}

# Core must reference nothing.
$coreProj = Get-Content "$slnDir/EMaigrator.Core/EMaigrator.Core.csproj" -Raw
if ($coreProj -match 'ProjectReference') { Fail "EMaigrator.Core must reference nothing" }

# Connectors + Infrastructure reference ONLY Core.
foreach ($m in @('EMaigrator.Connectors.Imap','EMaigrator.Connectors.Graph','EMaigrator.Connectors.Gmail','EMaigrator.Infrastructure')) {
  $refs = ([regex]::Matches((Get-Content "$slnDir/$m/$m.csproj" -Raw), 'ProjectReference Include="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
  foreach ($r in $refs) {
    if ($r -notmatch 'EMaigrator\.Core\.csproj$') { Fail "$m has illegal reference: $r" }
  }
}
Write-Host "structure-check OK"
exit 0
```

2. - [ ] Run it, expect FAIL: `pwsh -File src/structure-check.ps1` → exits non-zero with `EMaigrator.sln missing` because no solution exists yet.

3. - [ ] Minimal implementation. Create the solution and projects, then wire references. Run from the project root:
```powershell
dotnet new sln -n EMaigrator -o src
dotnet new classlib -n EMaigrator.Core -o src/EMaigrator.Core
dotnet new classlib -n EMaigrator.Connectors.Imap -o src/EMaigrator.Connectors.Imap
dotnet new classlib -n EMaigrator.Connectors.Graph -o src/EMaigrator.Connectors.Graph
dotnet new classlib -n EMaigrator.Connectors.Gmail -o src/EMaigrator.Connectors.Gmail
dotnet new classlib -n EMaigrator.Infrastructure -o src/EMaigrator.Infrastructure
dotnet new worker -n EMaigrator.Workers -o src/EMaigrator.Workers
dotnet new webapi -n EMaigrator.Api -o src/EMaigrator.Api
dotnet new console -n EMaigrator.Cli -o src/EMaigrator.Cli
dotnet new xunit -n EMaigrator.Core.Tests -o src/EMaigrator.Core.Tests
dotnet new xunit -n EMaigrator.Connectors.Imap.Tests -o src/EMaigrator.Connectors.Imap.Tests
dotnet new xunit -n EMaigrator.Connectors.Graph.Tests -o src/EMaigrator.Connectors.Graph.Tests
dotnet new xunit -n EMaigrator.Connectors.Gmail.Tests -o src/EMaigrator.Connectors.Gmail.Tests
dotnet new xunit -n EMaigrator.Infrastructure.Tests -o src/EMaigrator.Infrastructure.Tests
dotnet new xunit -n EMaigrator.Infrastructure.IntegrationTests -o src/EMaigrator.Infrastructure.IntegrationTests
dotnet new xunit -n EMaigrator.Workers.Tests -o src/EMaigrator.Workers.Tests
dotnet new xunit -n EMaigrator.Workers.IntegrationTests -o src/EMaigrator.Workers.IntegrationTests
dotnet new xunit -n EMaigrator.Api.Tests -o src/EMaigrator.Api.Tests
dotnet new xunit -n EMaigrator.Cli.Tests -o src/EMaigrator.Cli.Tests

dotnet sln src/EMaigrator.sln add (Get-ChildItem -Recurse src -Filter *.csproj | ForEach-Object { $_.FullName })

# Delete template default classes that would conflict later (keep csproj only).
Remove-Item src/EMaigrator.Core/Class1.cs, src/EMaigrator.Connectors.Imap/Class1.cs, src/EMaigrator.Connectors.Graph/Class1.cs, src/EMaigrator.Connectors.Gmail/Class1.cs, src/EMaigrator.Infrastructure/Class1.cs -ErrorAction SilentlyContinue

# Reference graph — connectors + infra → Core only.
dotnet add src/EMaigrator.Connectors.Imap reference src/EMaigrator.Core
dotnet add src/EMaigrator.Connectors.Graph reference src/EMaigrator.Core
dotnet add src/EMaigrator.Connectors.Gmail reference src/EMaigrator.Core
dotnet add src/EMaigrator.Infrastructure reference src/EMaigrator.Core
# Composition roots.
dotnet add src/EMaigrator.Workers reference src/EMaigrator.Core src/EMaigrator.Infrastructure src/EMaigrator.Connectors.Imap src/EMaigrator.Connectors.Graph src/EMaigrator.Connectors.Gmail
dotnet add src/EMaigrator.Api reference src/EMaigrator.Core src/EMaigrator.Infrastructure src/EMaigrator.Workers
dotnet add src/EMaigrator.Cli reference src/EMaigrator.Core src/EMaigrator.Infrastructure src/EMaigrator.Workers src/EMaigrator.Connectors.Imap src/EMaigrator.Connectors.Graph src/EMaigrator.Connectors.Gmail
# Test projects reference their subject.
dotnet add src/EMaigrator.Core.Tests reference src/EMaigrator.Core
dotnet add src/EMaigrator.Connectors.Imap.Tests reference src/EMaigrator.Connectors.Imap
dotnet add src/EMaigrator.Connectors.Graph.Tests reference src/EMaigrator.Connectors.Graph
dotnet add src/EMaigrator.Connectors.Gmail.Tests reference src/EMaigrator.Connectors.Gmail
dotnet add src/EMaigrator.Infrastructure.Tests reference src/EMaigrator.Infrastructure
dotnet add src/EMaigrator.Infrastructure.IntegrationTests reference src/EMaigrator.Infrastructure
dotnet add src/EMaigrator.Workers.Tests reference src/EMaigrator.Workers
dotnet add src/EMaigrator.Workers.IntegrationTests reference src/EMaigrator.Workers
dotnet add src/EMaigrator.Api.Tests reference src/EMaigrator.Api
dotnet add src/EMaigrator.Cli.Tests reference src/EMaigrator.Cli
```

4. - [ ] Run it, expect PASS: `pwsh -File src/structure-check.ps1` → prints `structure-check OK`, exit 0; and `dotnet build src/EMaigrator.sln -c Release` → `Build succeeded`.

5. - [ ] Commit:
```powershell
git add src/ && git commit -m @'
chore(foundation): scaffold solution with all projects and dependency-rule references

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 2: Directory.Build.props — nullable, langversion, analyzers, warnaserror, central package versions

**Goal:** Add a repo-wide `Directory.Build.props` (and `Directory.Packages.props` for central package management) that enforces `net10.0`, nullable, C# 13, latest analyzers, and treats warnings as errors across every project.

**Files:**
- Create: `src/Directory.Build.props`
- Create: `src/Directory.Packages.props`

**Acceptance Criteria:**
- [ ] Every project compiles with `<Nullable>enable</Nullable>`, `<LangVersion>13.0</LangVersion>`, `<TargetFramework>net10.0</TargetFramework>`, `<ImplicitUsings>enable</ImplicitUsings>` inherited from `Directory.Build.props` (not duplicated per-csproj).
- [ ] `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<EnableNETAnalyzers>true</EnableNETAnalyzers>` with `<AnalysisLevel>latest-recommended</AnalysisLevel>` are set repo-wide.
- [ ] `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` is on; package versions live in `Directory.Packages.props`.
- [ ] A deliberately-introduced unused-variable warning fails the build (proving warnaserror).
- [ ] `dotnet build src/EMaigrator.sln -c Release` succeeds with `0 Warning(s)`.

**Verify:** `dotnet build src/EMaigrator.sln -c Release /p:TreatWarningsAsErrors=true` → `Build succeeded`, `0 Warning(s)`, `0 Error(s)`.

**Steps:**

1. - [ ] Write the failing test — a verification script that asserts the props exist and warnaserror is active by compiling a probe file. Create `src/props-check.ps1`:
```powershell
$ErrorActionPreference = 'Stop'
$slnDir = $PSScriptRoot
function Fail($m){ Write-Error $m; exit 1 }
if (-not (Test-Path "$slnDir/Directory.Build.props")) { Fail "Directory.Build.props missing" }
if (-not (Test-Path "$slnDir/Directory.Packages.props")) { Fail "Directory.Packages.props missing" }
$props = Get-Content "$slnDir/Directory.Build.props" -Raw
foreach ($needle in @('<Nullable>enable</Nullable>','<LangVersion>13.0</LangVersion>','<TargetFramework>net10.0</TargetFramework>','<TreatWarningsAsErrors>true</TreatWarningsAsErrors>','<EnableNETAnalyzers>true</EnableNETAnalyzers>')) {
  if ($props -notmatch [regex]::Escape($needle)) { Fail "Directory.Build.props missing: $needle" }
}
# Prove warnaserror: drop a probe with an unused local, build, expect FAILURE, then remove probe.
$probe = "$slnDir/EMaigrator.Core/__WarnProbe.cs"
Set-Content $probe "namespace EMaigrator.Core; internal static class __WarnProbe { static void M() { int unused = 5; } }"
dotnet build "$slnDir/EMaigrator.Core/EMaigrator.Core.csproj" -c Release 2>&1 | Out-Null
$built = $LASTEXITCODE
Remove-Item $probe -Force
if ($built -eq 0) { Fail "warnaserror NOT enforced: unused-variable probe compiled clean" }
Write-Host "props-check OK"
exit 0
```

2. - [ ] Run it, expect FAIL: `pwsh -File src/props-check.ps1` → exits non-zero with `Directory.Build.props missing`.

3. - [ ] Minimal implementation. Create `src/Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>13.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```
Create `src/Directory.Packages.props` (pins the libraries the later plans depend on; versions current as of 2026-06):
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <!-- Test stack (used now) -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="coverlet.collector" Version="6.0.2" />
    <PackageVersion Include="FluentAssertions" Version="6.12.2" />
    <PackageVersion Include="NSubstitute" Version="5.3.0" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
  </ItemGroup>
</Project>
```
Because `<TargetFramework>`, `<Nullable>`, etc. are now inherited, remove the now-duplicate properties from every generated `.csproj` so they centralize. Each test `.csproj` must convert its `<PackageReference>` lines to versionless form (central management). Replace each test project's `<ItemGroup>` package block with:
```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>
```
And from every generated `.csproj` (production and test) remove the per-project `<TargetFramework>`, `<LangVersion>`, `<Nullable>`, `<ImplicitUsings>` lines so the props file is the single source. A trimmed production csproj (`EMaigrator.Core.csproj`) becomes:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>EMaigrator.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

4. - [ ] Run it, expect PASS: `pwsh -File src/props-check.ps1` → `props-check OK`; `dotnet build src/EMaigrator.sln -c Release` → `Build succeeded`, `0 Warning(s)`.

5. - [ ] Commit:
```powershell
git add src/ && git commit -m @'
chore(foundation): add Directory.Build.props and central package management

Enforce net10.0, nullable, C#13, analyzers, warnaserror repo-wide.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 3: Architecture dependency-rule test (NetArchTest) + solution smoke test

**Goal:** Add an executable xUnit architecture test in `EMaigrator.Core.Tests` that fails if `EMaigrator.Core` ever depends on Infrastructure/Connectors/Workers/Api/Cli, plus a trivial smoke test proving the test harness runs.

**Files:**
- Create: `src/EMaigrator.Core/AssemblyMarker.cs`
- Create: `src/EMaigrator.Core.Tests/Architecture/DependencyRuleTests.cs`
- Create: `src/EMaigrator.Core.Tests/SmokeTests.cs`
- Modify: `src/EMaigrator.Core.Tests/EMaigrator.Core.Tests.csproj`

**Acceptance Criteria:**
- [ ] `EMaigrator.Core.Tests` references `NetArchTest.Rules` (versionless, central) and `EMaigrator.Infrastructure` + `EMaigrator.Connectors.Imap` + `EMaigrator.Connectors.Graph` + `EMaigrator.Connectors.Gmail` (so the test can name the forbidden assemblies — test-only references, not a Core dependency).
- [ ] `DependencyRuleTests.Core_DoesNotDependOn_AnyHigherLayer` passes (Core has no such dependency).
- [ ] `SmokeTests.Harness_Runs` passes.
- [ ] If a `using EMaigrator.Infrastructure;` is added to a Core source file, the architecture test FAILS (demonstrated then reverted in steps).

**Verify:** `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~DependencyRuleTests|FullyQualifiedName~SmokeTests"` → all pass (2 passed).

**Steps:**

1. - [ ] Write the failing test. Create `src/EMaigrator.Core.Tests/Architecture/DependencyRuleTests.cs`:
```csharp
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace EMaigrator.Core.Tests.Architecture;

public class DependencyRuleTests
{
    private const string CoreAssembly = "EMaigrator.Core";

    private static readonly string[] ForbiddenForCore =
    [
        "EMaigrator.Infrastructure",
        "EMaigrator.Connectors.Imap",
        "EMaigrator.Connectors.Graph",
        "EMaigrator.Connectors.Gmail",
        "EMaigrator.Workers",
        "EMaigrator.Api",
        "EMaigrator.Cli",
    ];

    [Fact]
    public void Core_DoesNotDependOn_AnyHigherLayer()
    {
        var coreAsm = typeof(EMaigrator.Core.AssemblyMarker).Assembly;

        var result = Types.InAssembly(coreAsm)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenForCore)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "EMaigrator.Core must reference nothing (DESIGN.md §15). Offending types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
```
Create `src/EMaigrator.Core.Tests/SmokeTests.cs`:
```csharp
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Harness_Runs()
    {
        var marker = typeof(EMaigrator.Core.AssemblyMarker).Assembly.GetName().Name;
        marker.Should().Be("EMaigrator.Core");
    }
}
```

2. - [ ] Run it, expect FAIL: `dotnet test src/EMaigrator.Core.Tests` → compile error `CS0246: The type or namespace name 'AssemblyMarker' does not exist` and `CS0246: ... 'NetArchTest'` — neither the marker type nor the package reference exists yet.

3. - [ ] Minimal implementation. Create `src/EMaigrator.Core/AssemblyMarker.cs`:
```csharp
namespace EMaigrator.Core;

/// <summary>Stable type for reflecting on the EMaigrator.Core assembly in tests.</summary>
public sealed class AssemblyMarker;
```
Add the NetArchTest package and the test-only references to `src/EMaigrator.Core.Tests/EMaigrator.Core.Tests.csproj` `<ItemGroup>`:
```xml
    <PackageReference Include="NetArchTest.Rules" />
    <ProjectReference Include="..\EMaigrator.Infrastructure\EMaigrator.Infrastructure.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Imap\EMaigrator.Connectors.Imap.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Graph\EMaigrator.Connectors.Graph.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Gmail\EMaigrator.Connectors.Gmail.csproj" />
```

4. - [ ] Run it, expect PASS: `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~DependencyRuleTests|FullyQualifiedName~SmokeTests"` → `Passed! - Failed: 0, Passed: 2`.

Then prove the architecture test actually catches a violation, using a deterministic negative control. A bare `<ProjectReference>` alone does NOT make NetArchTest fail — the compiler elides an unused reference, so no IL-level dependency is emitted. You must introduce a real reference from Core IL to an Infrastructure type. Do exactly the following three temporary edits, then revert all three.

   a. Create a temporary public type in Infrastructure — `src/EMaigrator.Infrastructure/__ViolationProbe.cs`:
```csharp
namespace EMaigrator.Infrastructure;

/// <summary>TEMPORARY negative-control probe. Delete after demonstrating the architecture test trips.</summary>
public sealed class __ViolationProbe;
```

   b. Add a temporary project reference to `src/EMaigrator.Core/EMaigrator.Core.csproj` so Core can see Infrastructure (inside the existing top-level `<Project>` element, add a new `<ItemGroup>`):
```xml
  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Infrastructure\EMaigrator.Infrastructure.csproj" />
  </ItemGroup>
```

   c. Create a temporary Core source file that emits IL referencing the Infrastructure type — `src/EMaigrator.Core/__Violation.cs`. Make the holder type and member `public` so the unused-private-member analyzers (CA1823/IDE0051) do not fail the build before NetArchTest runs — the negative control must fail at the architecture test, not at compile time:
```csharp
namespace EMaigrator.Core;

/// <summary>TEMPORARY negative-control. Public so analyzers do not flag it as unused. Delete after demonstrating the architecture test trips.</summary>
public static class __Violation
{
    public static readonly EMaigrator.Infrastructure.__ViolationProbe Probe = new();
}
```

   Run `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~DependencyRuleTests"` → expect **FAIL**: `Core_DoesNotDependOn_AnyHigherLayer` fails with the message `EMaigrator.Core must reference nothing (DESIGN.md §15). Offending types: EMaigrator.Core.__Violation`.

   Revert the negative control: delete `src/EMaigrator.Infrastructure/__ViolationProbe.cs`, delete `src/EMaigrator.Core/__Violation.cs`, and remove the temporary `<ItemGroup>`/`<ProjectReference>` from `src/EMaigrator.Core/EMaigrator.Core.csproj`. Re-run `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~DependencyRuleTests"` → expect **PASS** (`Failed: 0, Passed: 1`). Record both the failing and passing outputs in the task notes; commit only the reverted (passing) state.

5. - [ ] Commit:
```powershell
git add src/ && git commit -m @'
test(foundation): architecture dependency-rule test + harness smoke test

NetArchTest asserts EMaigrator.Core depends on no higher layer.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 4: Vite + React 19 + TypeScript web stub with Tailwind + shadcn

**Goal:** Scaffold `/web` as a Vite + React 19 + TS SPA with Tailwind v4 and shadcn/ui initialized, plus a Vitest smoke test rendering the app root.

**Files:**
- Create: `web/package.json`, `web/vite.config.ts`, `web/tsconfig.json`, `web/tsconfig.node.json`, `web/index.html`, `web/components.json`
- Create: `web/src/main.tsx`, `web/src/App.tsx`, `web/src/index.css`, `web/src/lib/utils.ts`
- Create: `web/src/vite-env.d.ts`, `web/vitest.setup.ts`
- Test: `web/src/App.test.tsx`

**Acceptance Criteria:**
- [ ] `npm --prefix web ci` (or `install`) succeeds; React 19 and TypeScript are dependencies.
- [ ] Tailwind v4 is wired via `@tailwindcss/vite`; `index.css` contains `@import "tailwindcss";`.
- [ ] `web/components.json` exists (shadcn config) and `web/src/lib/utils.ts` exports `cn`.
- [ ] `npm --prefix web run build` produces `web/dist/index.html`.
- [ ] `App.test.tsx` renders `<App />` and asserts the heading text — passes under Vitest.

**Verify:** `npm --prefix web run test -- --run` → Vitest reports `1 passed`; and `npm --prefix web run build` → `dist/index.html` written, exit 0.

**Steps:**

1. - [ ] Write the failing test first. Create `web/src/App.test.tsx`:
```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import App from "./App";

describe("App", () => {
  it("renders the EMaigrator heading", () => {
    render(<App />);
    expect(
      screen.getByRole("heading", { name: /emaigrator/i }),
    ).toBeInTheDocument();
  });
});
```

2. - [ ] Run it, expect FAIL: `npm --prefix web run test -- --run` → fails because `web/package.json` does not exist (`npm ERR! enoent`), and there is no Vitest/App to run.

3. - [ ] Minimal implementation. Create `web/package.json`:
```json
{
  "name": "emaigrator-web",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "preview": "vite preview",
    "lint": "eslint .",
    "test": "vitest"
  },
  "dependencies": {
    "class-variance-authority": "^0.7.1",
    "clsx": "^2.1.1",
    "lucide-react": "^0.469.0",
    "react": "^19.0.0",
    "react-dom": "^19.0.0",
    "tailwind-merge": "^2.6.0"
  },
  "devDependencies": {
    "@tailwindcss/vite": "^4.0.0",
    "@testing-library/jest-dom": "^6.6.3",
    "@testing-library/react": "^16.1.0",
    "@types/node": "^24.0.0",
    "@types/react": "^19.0.0",
    "@types/react-dom": "^19.0.0",
    "@vitejs/plugin-react": "^4.3.4",
    "jsdom": "^25.0.1",
    "tailwindcss": "^4.0.0",
    "typescript": "^5.7.2",
    "vite": "^6.0.5",
    "vitest": "^2.1.8"
  }
}
```
Create `web/vite.config.ts`:
```ts
/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./vitest.setup.ts"],
  },
});
```
Create `web/vitest.setup.ts`:
```ts
import "@testing-library/jest-dom/vitest";
```
Create `web/tsconfig.json`:
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "useDefineForClassFields": true,
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "types": ["vitest/globals", "@testing-library/jest-dom"],
    "baseUrl": ".",
    "paths": { "@/*": ["./src/*"] }
  },
  "include": ["src", "vitest.setup.ts"],
  "references": [{ "path": "./tsconfig.node.json" }]
}
```
Create `web/tsconfig.node.json`:
```json
{
  "compilerOptions": {
    "composite": true,
    "skipLibCheck": true,
    "module": "ESNext",
    "moduleResolution": "bundler",
    "allowSyntheticDefaultImports": true,
    "strict": true,
    "types": ["node"]
  },
  "include": ["vite.config.ts"]
}
```
Create `web/index.html`:
```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>EMaigrator</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```
Create `web/src/index.css`:
```css
@import "tailwindcss";
```
Create `web/src/main.tsx`:
```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
```
Create `web/src/App.tsx`:
```tsx
export default function App() {
  return (
    <main className="min-h-screen flex items-center justify-center">
      <h1 className="text-3xl font-semibold">EMaigrator</h1>
    </main>
  );
}
```
Create `web/src/lib/utils.ts` (shadcn `cn` helper):
```ts
import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
```
Create `web/src/vite-env.d.ts`:
```ts
/// <reference types="vite/client" />
```
Create `web/components.json` (shadcn config; `npx shadcn add <comp>` will use it later):
```json
{
  "$schema": "https://ui.shadcn.com/schema.json",
  "style": "new-york",
  "rsc": false,
  "tsx": true,
  "tailwind": {
    "config": "",
    "css": "src/index.css",
    "baseColor": "neutral",
    "cssVariables": true,
    "prefix": ""
  },
  "aliases": {
    "components": "@/components",
    "utils": "@/lib/utils",
    "ui": "@/components/ui",
    "lib": "@/lib",
    "hooks": "@/hooks"
  }
}
```
Then install: `npm --prefix web install`.

4. - [ ] Run it, expect PASS: `npm --prefix web run test -- --run` → `Test Files 1 passed (1)`, `Tests 1 passed (1)`; and `npm --prefix web run build` → writes `web/dist/index.html`, exit 0.

5. - [ ] Commit:
```powershell
git add web/ && git commit -m @'
chore(web): scaffold Vite + React 19 + TS SPA with Tailwind v4 and shadcn

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 5: /deploy docker-compose (Postgres + RabbitMQ + Redis) + Dockerfiles

**Goal:** Add `/deploy` with a four-service docker-compose (api, postgres, rabbitmq, redis), a multi-stage Dockerfile for the .NET app, an `.env.example`, and a deploy-check test asserting the compose config is valid and the parity services are present.

**Files:**
- Create: `deploy/docker-compose.yml`
- Create: `deploy/Dockerfile.api`
- Create: `deploy/Dockerfile.workers`
- Create: `deploy/.dockerignore`
- Create: `deploy/.env.example`
- Create: `deploy/deploy-check.ps1`

**Acceptance Criteria:**
- [ ] `docker compose -f deploy/docker-compose.yml config` validates (exit 0).
- [ ] Compose defines services `api`, `workers`, `postgres`, `rabbitmq`, `redis` with named volumes for postgres and rabbitmq data, and healthchecks on postgres/rabbitmq/redis.
- [ ] Images pinned: `postgres:17`, `rabbitmq:4-management`, `redis:7`.
- [ ] `Dockerfile.api` is multi-stage (`sdk` build → `aspnet` runtime) targeting `src/EMaigrator.Api`.
- [ ] `deploy/.env.example` is committed and `deploy/.env` is git-ignored (already covered by root `.gitignore`); no real secrets committed.
- [ ] `deploy-check.ps1` passes.

**Verify:** `docker compose -f deploy/docker-compose.yml config --quiet; pwsh -File deploy/deploy-check.ps1` → `config` exits 0, `deploy-check OK`.

**Steps:**

1. - [ ] Write the failing test. Create `deploy/deploy-check.ps1`:
```powershell
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
function Fail($m){ Write-Error $m; exit 1 }
$compose = "$dir/docker-compose.yml"
if (-not (Test-Path $compose)) { Fail "docker-compose.yml missing" }
# Validate the compose schema via docker.
docker compose -f $compose config --quiet
if ($LASTEXITCODE -ne 0) { Fail "docker compose config failed" }
$text = Get-Content $compose -Raw
foreach ($svc in @('postgres','rabbitmq','redis','api','workers')) {
  if ($text -notmatch "(?m)^\s{2,4}$svc\s*:") { Fail "service missing: $svc" }
}
foreach ($img in @('postgres:17','rabbitmq:4-management','redis:7')) {
  if ($text -notmatch [regex]::Escape($img)) { Fail "pinned image missing: $img" }
}
if (-not (Test-Path "$dir/Dockerfile.api")) { Fail "Dockerfile.api missing" }
if (-not (Test-Path "$dir/.env.example")) { Fail ".env.example missing" }
# No real secrets: .env must NOT be committed.
if (Test-Path "$dir/.env") { Fail "deploy/.env must not be committed" }
Write-Host "deploy-check OK"
exit 0
```

2. - [ ] Run it, expect FAIL: `pwsh -File deploy/deploy-check.ps1` → exits non-zero with `docker-compose.yml missing`.

3. - [ ] Minimal implementation. Create `deploy/docker-compose.yml`:
```yaml
name: emaigrator

services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: ${POSTGRES_USER:-emaigrator}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-emaigrator}
      POSTGRES_DB: ${POSTGRES_DB:-emaigrator}
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-emaigrator}"]
      interval: 5s
      timeout: 5s
      retries: 10

  rabbitmq:
    image: rabbitmq:4-management
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER:-emaigrator}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:-emaigrator}
    ports:
      - "5672:5672"
      - "15672:15672"
    volumes:
      - rabbitdata:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 10s
      timeout: 5s
      retries: 10

  redis:
    image: redis:7
    command: ["redis-server", "--appendonly", "yes"]
    ports:
      - "6379:6379"
    volumes:
      - redisdata:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 10

  api:
    build:
      context: ..
      dockerfile: deploy/Dockerfile.api
    environment:
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}
      ConnectionStrings__Postgres: Host=postgres;Port=5432;Database=${POSTGRES_DB:-emaigrator};Username=${POSTGRES_USER:-emaigrator};Password=${POSTGRES_PASSWORD:-emaigrator}
      RabbitMq__Host: rabbitmq
      RabbitMq__Username: ${RABBITMQ_USER:-emaigrator}
      RabbitMq__Password: ${RABBITMQ_PASSWORD:-emaigrator}
      Redis__Configuration: redis:6379
    ports:
      - "8080:8080"
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
      redis:
        condition: service_healthy

  workers:
    build:
      context: ..
      dockerfile: deploy/Dockerfile.workers
    environment:
      DOTNET_ENVIRONMENT: ${DOTNET_ENVIRONMENT:-Production}
      ConnectionStrings__Postgres: Host=postgres;Port=5432;Database=${POSTGRES_DB:-emaigrator};Username=${POSTGRES_USER:-emaigrator};Password=${POSTGRES_PASSWORD:-emaigrator}
      RabbitMq__Host: rabbitmq
      RabbitMq__Username: ${RABBITMQ_USER:-emaigrator}
      RabbitMq__Password: ${RABBITMQ_PASSWORD:-emaigrator}
      Redis__Configuration: redis:6379
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
      redis:
        condition: service_healthy

volumes:
  pgdata:
  rabbitdata:
  redisdata:
```
Create `deploy/Dockerfile.api`:
```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY src/Directory.Build.props src/Directory.Packages.props src/EMaigrator.sln ./src/
COPY src/ ./src/
RUN dotnet restore src/EMaigrator.Api/EMaigrator.Api.csproj
RUN dotnet publish src/EMaigrator.Api/EMaigrator.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "EMaigrator.Api.dll"]
```
Create `deploy/Dockerfile.workers`:
```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY src/Directory.Build.props src/Directory.Packages.props src/EMaigrator.sln ./src/
COPY src/ ./src/
RUN dotnet restore src/EMaigrator.Workers/EMaigrator.Workers.csproj
RUN dotnet publish src/EMaigrator.Workers/EMaigrator.Workers.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "EMaigrator.Workers.dll"]
```
Create `deploy/.dockerignore`:
```gitignore
**/bin/
**/obj/
**/node_modules/
**/dist/
**/.git/
**/TestResults/
web/
docs/
design_handoff_emaigrator/
```
Create `deploy/.env.example` (placeholders only — never real secrets):
```dotenv
# Copy to deploy/.env and set real values for local self-host. deploy/.env is git-ignored.
POSTGRES_USER=emaigrator
POSTGRES_PASSWORD=change-me
POSTGRES_DB=emaigrator
RABBITMQ_USER=emaigrator
RABBITMQ_PASSWORD=change-me
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
```

4. - [ ] Run it, expect PASS: `docker compose -f deploy/docker-compose.yml config --quiet` → exit 0; `pwsh -File deploy/deploy-check.ps1` → `deploy-check OK`.

5. - [ ] Commit:
```powershell
git add deploy/ && git commit -m @'
chore(deploy): docker-compose (postgres/rabbitmq/redis) + api/workers Dockerfiles

Four-container parity environment; secrets via .env.example only.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 6: CI workflow — build, test+coverage, vulnerability audit, web build+vitest

**Goal:** Add a GitHub Actions workflow that builds the solution, runs all .NET tests with coverage, runs `dotnet list package --vulnerable --include-transitive` and **fails the job on any finding**, and builds the web app + runs Vitest.

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `scripts/check-vulnerable.ps1`
- Create: `src/EMaigrator.Core.Tests/CiScriptTests.cs`

**Acceptance Criteria:**
- [ ] `ci.yml` has a `dotnet` job: `setup-dotnet` (10.x), `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release --collect:"XPlat Code Coverage"`, then runs `scripts/check-vulnerable.ps1`.
- [ ] `scripts/check-vulnerable.ps1` runs `dotnet list package --vulnerable --include-transitive`, and exits non-zero if the output contains any vulnerability rows (matched by the `>` advisory marker / "has the following vulnerable packages").
- [ ] `ci.yml` has a `web` job: `setup-node` (24.x), `npm ci`, `npm run build`, `npm run test -- --run`.
- [ ] A unit test verifies `check-vulnerable.ps1` logic: given a fixture of clean output it returns 0; given a fixture containing a vulnerability row it returns non-zero.
- [ ] Workflow is valid YAML.

**Verify:** `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~CiScriptTests"` → all pass (2 passed); and `pwsh -Command "(Get-Content .github/workflows/ci.yml -Raw); 'yaml-present'"` → file printed.

**Steps:**

1. - [ ] Write the failing test. Create `src/EMaigrator.Core.Tests/CiScriptTests.cs` (exercises the real `check-vulnerable.ps1` against canned `dotnet list package` outputs via stdin):
```csharp
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests;

public class CiScriptTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "scripts", "check-vulnerable.ps1")))
            dir = dir.Parent;
        dir.Should().NotBeNull("scripts/check-vulnerable.ps1 must exist above the test bin dir");
        return dir!.FullName;
    }

    private static int RunCheck(string listOutput)
    {
        var root = RepoRoot();
        var script = Path.Combine(root, "scripts", "check-vulnerable.ps1");
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, listOutput);
        try
        {
            var psi = new ProcessStartInfo("pwsh",
                $"-NoProfile -File \"{script}\" -InputFile \"{tmp}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Clean_Output_Returns_Zero()
    {
        const string clean = "The given project `EMaigrator.Core` has no vulnerable packages given the current sources.";
        RunCheck(clean).Should().Be(0);
    }

    [Fact]
    public void Vulnerable_Output_Returns_NonZero()
    {
        const string vulnerable =
            "Project `EMaigrator.Api` has the following vulnerable packages\n" +
            "   [net10.0]:\n" +
            "   Top-level Package      Requested   Resolved   Severity   Advisory URL\n" +
            "   > SomePackage          1.0.0       1.0.0      High       https://github.com/advisories/GHSA-xxxx\n";
        RunCheck(vulnerable).Should().NotBe(0);
    }
}
```

2. - [ ] Run it, expect FAIL: `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~CiScriptTests"` → both fail because `scripts/check-vulnerable.ps1` does not exist (assertion `dir.Should().NotBeNull()` fails).

3. - [ ] Minimal implementation. Create `scripts/check-vulnerable.ps1`:
```powershell
<#
.SYNOPSIS
  Fails (non-zero exit) if `dotnet list package --vulnerable` reports any vulnerability.
.PARAMETER InputFile
  Optional path to a file containing pre-captured `dotnet list package` output (for tests).
  When omitted, this script runs the real command against src/EMaigrator.sln.
#>
param([string]$InputFile)

$ErrorActionPreference = 'Stop'

if ($InputFile) {
  $output = Get-Content -Raw -Path $InputFile
} else {
  $sln = Join-Path $PSScriptRoot '..' 'src' 'EMaigrator.sln'
  dotnet restore $sln | Out-Null
  $output = dotnet list $sln package --vulnerable --include-transitive 2>&1 | Out-String
}

Write-Host $output

# A vulnerability is present when dotnet prints "has the following vulnerable packages"
# and/or advisory rows marked with a leading ">". Clean output says "no vulnerable packages".
$hasVuln = ($output -match 'has the following vulnerable packages') `
  -or ($output -match '(?m)^\s*>\s')

if ($hasVuln) {
  Write-Error 'Vulnerable NuGet packages detected — failing the build.'
  exit 1
}
Write-Host 'No vulnerable packages detected.'
exit 0
```
Create `.github/workflows/ci.yml`:
```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

permissions:
  contents: read

jobs:
  dotnet:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - name: Restore
        run: dotnet restore src/EMaigrator.sln
      - name: Build
        run: dotnet build src/EMaigrator.sln -c Release --no-restore
      - name: Test with coverage
        run: dotnet test src/EMaigrator.sln -c Release --no-build --collect:"XPlat Code Coverage" --results-directory ./TestResults
      - name: Vulnerability audit (fails on any finding)
        shell: pwsh
        run: ./scripts/check-vulnerable.ps1

  web:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: web
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: "24.x"
          cache: npm
          cache-dependency-path: web/package-lock.json
      - run: npm ci
      - run: npm run build
      - run: npm run test -- --run
```

4. - [ ] Run it, expect PASS: `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~CiScriptTests"` → `Passed! - Failed: 0, Passed: 2`. (Requires `pwsh` on PATH; it is present on this machine and on GitHub `ubuntu-latest`.)

5. - [ ] Commit:
```powershell
git add .github/ scripts/ src/EMaigrator.Core.Tests/CiScriptTests.cs && git commit -m @'
ci(foundation): build/test/coverage + vulnerable-package gate + web build/vitest

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 7: Functional Verification — solution builds and the full test suite runs green

**Goal:** Prove the subsystem's headline behavior end-to-end: the entire solution restores, builds with zero warnings, and the full xUnit suite (including the architecture and smoke tests) runs green; the web app builds and Vitest passes.

**Files:**
- Create: `src/EMaigrator.Core.Tests/FoundationAcceptanceTests.cs`

**Acceptance Criteria:**
- [ ] `dotnet build src/EMaigrator.sln -c Release` → `Build succeeded`, `0 Warning(s)`, `0 Error(s)`.
- [ ] `dotnet test src/EMaigrator.sln -c Release` → all tests pass, `Failed: 0`.
- [ ] `npm --prefix web run build` produces `web/dist/index.html`; `npm --prefix web run test -- --run` → all pass.
- [ ] `FoundationAcceptanceTests.All_Expected_Assemblies_Load` asserts all 8 production assemblies are loadable and named as in CONTRACTS §8.

**Verify:** `dotnet test src/EMaigrator.sln -c Release` → `Passed!` with `Failed: 0`; and `npm --prefix web run test -- --run` → `Tests` all passed.

**Steps:**

1. - [ ] Write the failing test. Create `src/EMaigrator.Core.Tests/FoundationAcceptanceTests.cs`:
```csharp
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests;

public class FoundationAcceptanceTests
{
    private static readonly string[] ProductionAssemblies =
    [
        "EMaigrator.Core",
        "EMaigrator.Connectors.Imap",
        "EMaigrator.Connectors.Graph",
        "EMaigrator.Connectors.Gmail",
        "EMaigrator.Infrastructure",
        "EMaigrator.Workers",
        "EMaigrator.Api",
        "EMaigrator.Cli",
    ];

    [Fact]
    public void All_Expected_Assemblies_Load()
    {
        foreach (var name in ProductionAssemblies)
        {
            var act = () => Assembly.Load(new AssemblyName(name));
            act.Should().NotThrow($"{name} must be a loadable assembly in the solution");
        }
    }
}
```
Note: this requires `EMaigrator.Core.Tests` to reference `EMaigrator.Workers`, `EMaigrator.Api`, and `EMaigrator.Cli` so they are copied to the test output. Add to `src/EMaigrator.Core.Tests/EMaigrator.Core.Tests.csproj`:
```xml
    <ProjectReference Include="..\EMaigrator.Workers\EMaigrator.Workers.csproj" />
    <ProjectReference Include="..\EMaigrator.Api\EMaigrator.Api.csproj" />
    <ProjectReference Include="..\EMaigrator.Cli\EMaigrator.Cli.csproj" />
```
(`EMaigrator.Core` itself still references nothing — these are *test-project* references; the architecture test in Task 3 already guards Core proper.)

2. - [ ] Run it, expect FAIL: `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~FoundationAcceptanceTests"` → fails to load `EMaigrator.Api`/`EMaigrator.Cli` (FileNotFoundException) until the test-project references above are added.

3. - [ ] Minimal implementation: add the three `<ProjectReference>` lines shown above to `src/EMaigrator.Core.Tests/EMaigrator.Core.Tests.csproj`. The `Api`/`Workers`/`Cli` assemblies are then copied into the test bin and `Assembly.Load` succeeds.

4. - [ ] Run it, expect PASS: `dotnet test src/EMaigrator.sln -c Release` → `Passed! - Failed: 0`; `npm --prefix web run build` → `dist/index.html`; `npm --prefix web run test -- --run` → all passed.

5. - [ ] Commit:
```powershell
git add src/ && git commit -m @'
test(foundation): functional acceptance — solution builds and all assemblies load

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```

---

### Task 8: Security Verification — vulnerability gate fails on a finding + no secrets committed

**Goal:** Prove this plan's security focus from the INDEX per-plan table: CI's `dotnet list package --vulnerable` gate actually fails the build on a real finding, and no secrets are committed (`.gitignore` verified).

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Core.Tests/Security/VulnerabilityGateTests.cs`
- Create: `src/EMaigrator.Core.Tests/Security/SecretsHygieneTests.cs`

**Acceptance Criteria:**
- [ ] Running `scripts/check-vulnerable.ps1 -InputFile <fixture-with-a-real-advisory-row>` exits **non-zero** (gate trips on a finding) — captured in test output.
- [ ] Running `scripts/check-vulnerable.ps1 -InputFile <fixture-clean>` exits **zero** (no false positive) — captured.
- [ ] The live audit over the actual solution passes today: `pwsh -File scripts/check-vulnerable.ps1` exits 0 over `src/EMaigrator.sln` (no current vulnerable packages) — captured.
- [ ] `.gitignore` contains entries for `.env`, `.env.*`, `*.pem`, `secrets.json`, `appsettings.*.local.json` — verified by test reading the file.
- [ ] `git ls-files` shows **zero** tracked files matching secret patterns (`.env` (not `.env.example`), `*.pem`, `secrets.json`, `appsettings.*.local.json`) — verified by test.
- [ ] No real password literal appears in committed config: `deploy/.env.example` values are placeholders (`change-me`), and `deploy/.env` is not tracked.

**Verify:** `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~Security"` → all pass (covers gate-trips-on-finding, gate-clean, live-audit-clean, gitignore, and zero-tracked-secrets).

**Steps:**

1. - [ ] Write the failing tests. Create `src/EMaigrator.Core.Tests/Security/VulnerabilityGateTests.cs`:
```csharp
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests.Security;

public class VulnerabilityGateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "scripts", "check-vulnerable.ps1")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static (int code, string output) RunCheck(string? inputFileContent, bool live)
    {
        var root = RepoRoot();
        var script = Path.Combine(root, "scripts", "check-vulnerable.ps1");
        string args;
        string? tmp = null;
        if (live)
        {
            args = $"-NoProfile -File \"{script}\"";
        }
        else
        {
            tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, inputFileContent!);
            args = $"-NoProfile -File \"{script}\" -InputFile \"{tmp}\"";
        }
        try
        {
            var psi = new ProcessStartInfo("pwsh", args)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root };
            using var p = Process.Start(psi)!;
            var outText = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, outText);
        }
        finally { if (tmp is not null) File.Delete(tmp); }
    }

    [Fact]
    public void Gate_Trips_On_Real_Advisory_Row()
    {
        const string vulnerable =
            "Project `EMaigrator.Infrastructure` has the following vulnerable packages\n" +
            "   [net10.0]:\n" +
            "   Top-level Package   Requested   Resolved   Severity   Advisory URL\n" +
            "   > Npgsql            4.0.0       4.0.0      Critical   https://github.com/advisories/GHSA-abcd-1234\n";
        var (code, output) = RunCheck(vulnerable, live: false);
        code.Should().NotBe(0, "the gate MUST fail the build on any vulnerability finding");
        output.Should().Contain("Vulnerable NuGet packages detected");
    }

    [Fact]
    public void Gate_Passes_On_Clean_Output()
    {
        const string clean = "The given project has no vulnerable packages given the current sources.";
        var (code, _) = RunCheck(clean, live: false);
        code.Should().Be(0);
    }

    [Fact]
    public void Live_Audit_Over_Solution_Is_Clean_Today()
    {
        var (code, output) = RunCheck(null, live: true);
        code.Should().Be(0, $"the real solution must have no vulnerable packages. Audit output:\n{output}");
    }
}
```
Create `src/EMaigrator.Core.Tests/Security/SecretsHygieneTests.cs`:
```csharp
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests.Security;

public class SecretsHygieneTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".gitignore")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    [Fact]
    public void Gitignore_Covers_Secret_Patterns()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepoRoot(), ".gitignore"));
        foreach (var pattern in new[] { ".env", ".env.*", "*.pem", "secrets.json", "appsettings.*.local.json" })
            gitignore.Should().Contain(pattern, $".gitignore must ignore {pattern}");
    }

    [Fact]
    public void No_Secret_Files_Are_Tracked()
    {
        var root = RepoRoot();
        var psi = new ProcessStartInfo("git", "ls-files")
        { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = root };
        using var p = Process.Start(psi)!;
        var tracked = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        p.WaitForExit();

        var offenders = tracked.Where(f =>
            (f.EndsWith(".pem")) ||
            (f.EndsWith("secrets.json")) ||
            (Path.GetFileName(f) == ".env") ||
            (Path.GetFileName(f).StartsWith(".env.") && Path.GetFileName(f) != ".env.example") ||
            (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^appsettings\..*\.local\.json$"))
        ).ToList();

        offenders.Should().BeEmpty("no secret files may be committed; found: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EnvExample_Has_No_Real_Secrets()
    {
        var example = Path.Combine(RepoRoot(), "deploy", ".env.example");
        File.Exists(example).Should().BeTrue();
        var text = File.ReadAllText(example);
        // Placeholder convention: passwords are "change-me", never a real value.
        text.Should().Contain("POSTGRES_PASSWORD=change-me");
        text.Should().Contain("RABBITMQ_PASSWORD=change-me");
    }
}
```

2. - [ ] Run them, expect FAIL: `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~Security"` → fails first to compile/locate until the files are added, then `Live_Audit_Over_Solution_Is_Clean_Today` and `Gate_Trips_On_Real_Advisory_Row` exercise the real script. (If the live audit ever reports a finding, that is a genuine failure to fix by bumping the offending pinned version in `Directory.Packages.props` — never by weakening the gate.)

3. - [ ] Minimal implementation: no new production code is required — the gate (`scripts/check-vulnerable.ps1`, Task 6) and `.gitignore` (already present) provide the behavior. If the gitignore test reveals a missing pattern, add it to `.gitignore`. If a tracked-secret offender is found, `git rm --cached` it and add the pattern. If the live audit reports a vulnerable transitive package, raise the pinned version in `src/Directory.Packages.props` until the audit is clean.

4. - [ ] Run them, expect PASS: `dotnet test src/EMaigrator.Core.Tests --filter "FullyQualifiedName~Security"` → `Passed! - Failed: 0`. Capture the console output of `Live_Audit_Over_Solution_Is_Clean_Today` (the embedded `dotnet list package --vulnerable` text showing "no vulnerable packages") and the non-zero exit demonstration from `Gate_Trips_On_Real_Advisory_Row` into the task close-out notes.

5. - [ ] Commit:
```powershell
git add src/EMaigrator.Core.Tests/Security/ .gitignore && git commit -m @'
test(security): prove vulnerable-package gate trips on findings; no secrets committed

USER-ORDERED GATE: dependency audit fails build on any advisory; .gitignore
covers secret patterns; git ls-files shows zero tracked secret files.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
'@
```
