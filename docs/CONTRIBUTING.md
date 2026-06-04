# Contributing to EMaigrator

> First-touch guide for humans and agents working in `/src`. The architecture lives in `DESIGN.md`,
> `ARCHITECTURE.md`, and the **frozen** `docs/superpowers/plans/2026-06-01-emaigrator-CONTRACTS.md`.
> This file captures the *mechanics* that bite on the first build and the first new package/host —
> the things that are true of the repo but not obvious from any single file.

## The build is strict (read this before adding a package or a project)

`src/Directory.Build.props` and `src/Directory.Packages.props` impose one ruleset on every project.
Most "mystery" build failures are one of these:

1. **Central Package Management is on.** Every `<PackageReference>` is **versionless**; the version
   lives only in `src/Directory.Packages.props` as a `<PackageVersion>`. SDK templates emit *versioned*
   references — strip the version or `restore` fails. Transitive pinning is enabled
   (`CentralPackageTransitivePinningEnabled`), so a vulnerable transitive can be force-overridden
   centrally (e.g. `Microsoft.Kiota.Abstractions` is pinned to dodge an advisory).

2. **Warnings are errors + latest-recommended analyzers** (`TreatWarningsAsErrors`,
   `EnableNETAnalyzers`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`). Stock template
   code routinely trips `CA1848` (use `LoggerMessage`), `CA1305` (`IFormatProvider`), `CA1727`,
   `CA1062` (`ArgumentNullException.ThrowIfNull`), `CA1852`, `CA1859`. **Fix the code — do not weaken
   the gate.** The only sanctioned relaxation: test projects (name ends with `Tests`) get `CA1707`/
   `CA1861` `NoWarn` via an `MSBuildProjectName.EndsWith('Tests')` condition.

3. **NuGet vulnerability audit is error-level** (`NU1902`/`NU1903`), transitively. Adding a package
   with a known advisory **fails the build**. Check the GitHub advisory's first-patched version and pin
   it centrally (e.g. MailKit/MimeKit ≥ 4.16.0).

4. **Use the classic `.sln`** (`dotnet new sln --format sln`). The .NET 10 SDK now defaults to `.slnx`,
   but CI, `src/structure-check.ps1`, the Dockerfiles, and `scripts/check-vulnerable.ps1` all reference
   `src/EMaigrator.sln` by path.

5. **Test projects already global-`using Xunit`** via `<Using Include="Xunit"/>` in the csproj. A
   hand-written `global using Xunit;` is a `CS0105` duplicate (an error under warnings-as-errors).

6. **EF-generated migrations** are excluded from the analyzer gate by a scoped
   `Data/Migrations/.editorconfig` (`generated_code = true`). Never hand-edit generated migrations or
   weaken the global gate to satisfy them.

7. **Testcontainers (4.12)** needs the image in the builder constructor
   (`new PostgreSqlBuilder("postgres:17-alpine")`); the parameterless ctor + `WithImage` is `[Obsolete]`
   → `CS0618` under the strict gate.

8. **MassTransit stays on the 8.x line.** v9+ is a commercial license, which violates the
   self-host/cloud-parity rule (convention 7). The 8.x pin forces `RabbitMQ.Client ≥ 7.2.1` (async
   `IConnection`). Every consumer needs `CA1848` (source-generated `[LoggerMessage]`) and `CA1062`
   (`ThrowIfNull(context)`).

## Composition seams (how the engine is wired into a host)

All three hosts (API, CLI, in-process worker) assemble the **same** engine through two DI extensions.
The wiring is invisible at the call site and a re-order breaks things at runtime, not compile time:

- `services.AddInfrastructure(config, registerBus: false)` — registers `ILedger` / `ISecretStore` /
  `IRateLimiter` / `IJobOrchestrator` + Postgres/Redis/RabbitMQ health checks. It does **not** register
  `IPreflightAnalyzer` or `IErrorCatalog` — the host adds those explicitly.
- **There is exactly one MassTransit bus per process.** Pass `registerBus: false` whenever the host
  owns the bus so it can attach Worker consumers — **every** host (API/Workers/CLI) does. Only a
  bus-free host would use the default `true`.
- `services.AddEmaigratorWorkers(config)` owns the bus + the six consumers + `AddWorkerDataSeams`, and
  **re-registers `IJobOrchestrator` as a singleton bound to `IBus`**. Because last-registration-wins,
  **`AddEmaigratorWorkers` must be called *after* `AddInfrastructure`** — commands/services resolve the
  orchestrator from the *root* provider, and a scoped publish endpoint fails scope validation.
- The Workers bus reads its broker from `ConnectionStrings:RabbitMq`; the API's own bus reads
  `Infrastructure:RabbitMqConnectionString`. (Different keys — see `deploy/docker-compose.yml`.)

Composition roots: `EMaigrator.Workers/Program.cs`, `EMaigrator.Cli/Hosting/CliHostBuilder.cs`,
`EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`.

> The frozen plan texts use older names (`AddEMaigratorInfrastructure(..., inProcessWorker: true)`) —
> that is drift. Bind to the real signatures above.

## Adding a connector

See **[`connectors/authoring-a-connector.md`](connectors/authoring-a-connector.md)** for the full
recipe (the ~10-file skeleton, the `TryAddEnumerable` registration, the three composition-root call
sites, the secret/settings key table, and the credential-free error-normalization rule).

## Tests & CI — what actually runs where

- `*.Tests` projects are pure unit tests (no Docker). `*.IntegrationTests` use **Testcontainers** and
  **require Docker Desktop running** — their fixtures call `StartAsync()` unconditionally (no skip
  guard), so without Docker they **hard-fail**, they don't skip.
- **CI (`.github/workflows/ci.yml`) does NOT provision Docker.** It runs `dotnet build/test
  src/EMaigrator.sln` (the `.sln` includes the Docker-bound `*.IntegrationTests`, which therefore don't
  meaningfully run in CI) + `check-vulnerable.ps1`, and the web job runs only `build` + `test --run`.
  It does **not** run `npm run e2e`, `scan:bundle`, `lint`, or the structure/props/deploy check scripts.
  Those are **local/agent gates** — run them with Docker up before claiming a plan green. "CI is green"
  ≠ "the integration suite passed."
- Of the four `*-check.ps1` scripts, only `scripts/check-vulnerable.ps1` is wired into tests + CI.
  `src/structure-check.ps1`, `src/props-check.ps1`, and `deploy/deploy-check.ps1` are one-shot Plan-01
  acceptance scripts that nothing re-runs — run them by hand after changing the project graph, build
  props, or `docker-compose`. The continuously-enforced guards are the **NetArchTest** dependency-rule
  tests in `EMaigrator.Core.Tests` and the vulnerability gate.

## Working in this repo (Windows specifics)

1. **Run the `*-check.ps1` verify scripts through the Bash tool** as `pwsh -NoProfile -File <path>`.
   Launching `pwsh -File` from inside the PowerShell tool (already a pwsh session) backgrounds/hangs in
   this harness and produces empty output. `dotnet`/`npm`/`git` are fine in either tool.
2. **Worktree isolation is unreliable here** — the path contains a space (`Personal Projects`), so the
   agent harness can report the repo as non-git and `EnterWorktree` / `isolation: 'worktree'` fail. Use
   raw `git` via the Bash tool or a feature branch in the main checkout.
3. **Loose-ref corruption recovery.** If a build fails with SourceLink `Invalid reference: "   "` or
   `git rev-parse HEAD` is ambiguous and `git status` shows every file as a staged add, a loose ref got
   zeroed mid-session (seen once when Docker Desktop restarted during a git op). Recovery is
   non-destructive: confirm the last SHA from `.git/logs/HEAD`, then rewrite `.git/refs/heads/<branch>`
   directly (`git update-ref` can't lock a corrupt ref). Commit-after-every-task is the prevention.

## Commits

Conventional Commits, one per task, with the trailer
`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. When a plan/phase changes state, update the
`BUILD-STATUS` table in `CLAUDE.md` in the same commit (see "Keeping this current" there).
