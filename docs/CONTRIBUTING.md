# Contributing to EMaigrator

Thanks for your interest in contributing! EMaigrator is the open-source, self-hostable email-migration
engine. This guide covers how to get set up, the development workflow, and the repo-specific mechanics that
bite on a first build.

By participating you agree to abide by our [Code of Conduct](../CODE_OF_CONDUCT.md). Contributions are
accepted under the project's [Apache-2.0 license](../LICENSE).

## Ways to contribute

- **Report a bug** or request a feature via [GitHub Issues](../../issues) (templates provided).
- **Report a security vulnerability** privately — see [SECURITY.md](../SECURITY.md). Do **not** open a
  public issue for security reports.
- **Send a pull request** for a fix or feature (see the workflow below).
- Keep **hosted-only** concerns out — billing/quotas, multi-tenant orchestration, branded OAuth, and AI
  fallback belong in the separate hosted layer, not this engine.

## Getting set up

Prerequisites: **.NET 10 SDK**, **Node 24+**, and **Docker Desktop** (for the integration test suites and
the compose stack).

```bash
git clone https://github.com/waleedzafar68/EMaigrator.git
cd EMaigrator

# Build + unit-test the engine
dotnet build src/EMaigrator.sln -c Release
dotnet test  src/EMaigrator.sln -c Release        # integration suites need Docker running

# Frontend
npm --prefix web ci
npm --prefix web run test -- --run

# Run the whole stack locally (see README for the .env step)
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d   # app at http://localhost:3000
```

## Development workflow

1. Fork the repo and create a topic branch off `main` (`git checkout -b fix/short-description`).
2. Make your change with tests. Follow **TDD** where practical; keep commits focused.
3. Use **[Conventional Commits](https://www.conventionalcommits.org/)** (`fix:`, `feat:`, `docs:`,
   `refactor:`, `test:`, `chore:`, `ci:`).
4. Make sure the gates pass locally (see *Tests & CI* below).
5. Open a PR against `main`, fill in the template, and link any related issue.

## The build is strict (read before adding a package or project)

`src/Directory.Build.props` + `src/Directory.Packages.props` impose one ruleset on every project. Most
"mystery" build failures are one of these:

1. **Central Package Management is on.** Every `<PackageReference>` is **versionless**; the version lives
   only in `src/Directory.Packages.props` as a `<PackageVersion>`. SDK templates emit *versioned*
   references — strip the version or `restore` fails. Transitive pinning is enabled, so a vulnerable
   transitive can be force-overridden centrally.
2. **Warnings are errors + latest-recommended analyzers.** Stock template code routinely trips `CA1848`
   (`LoggerMessage`), `CA1305` (`IFormatProvider`), `CA1062` (`ArgumentNullException.ThrowIfNull`), etc.
   **Fix the code — do not weaken the gate.** Only test projects (name ends with `Tests`) relax
   `CA1707`/`CA1861`.
3. **NuGet vulnerability audit is error-level** (`NU1902`/`NU1903`), transitively. Adding a package with a
   known advisory **fails the build** — pin the first-patched version centrally.
4. **Use the classic `.sln`** (`dotnet new sln --format sln`); the .NET 10 SDK defaults to `.slnx`, but CI,
   the Dockerfiles, and `scripts/check-vulnerable.ps1` reference `src/EMaigrator.sln` by path.
5. **Test projects already global-`using Xunit`** via the csproj — a hand-written `global using Xunit;` is
   a `CS0105` duplicate (an error here).
6. **EF-generated migrations** are excluded from the analyzer gate via a scoped `.editorconfig`; never
   hand-edit generated migrations or weaken the global gate to satisfy them.
7. **Testcontainers** needs the image in the builder constructor (`new PostgreSqlBuilder("postgres:17")`);
   the parameterless ctor + `WithImage` is `[Obsolete]` → `CS0618` under the strict gate.
8. **MassTransit stays on the 8.x line** — v9+ is a commercial license, which violates the self-host
   parity rule. The 8.x pin requires `RabbitMQ.Client ≥ 7.2.1` (async `IConnection`).

## Composition seams (how the engine is wired into a host)

All three hosts (API, CLI, in-process worker) assemble the **same** engine through two DI extensions. The
order is invisible at the call site but breaks at runtime, not compile time:

- `services.AddInfrastructure(config, registerBus: false)` registers `ILedger`/`ISecretStore`/
  `IRateLimiter`/`IJobOrchestrator` + health checks. It does **not** register `IPreflightAnalyzer` or
  `IErrorCatalog` — the host adds those.
- **Exactly one MassTransit bus per process.** Pass `registerBus: false` whenever the host owns the bus so
  it can attach the Worker consumers — every host does.
- `services.AddEmaigratorWorkers(config)` owns the bus + consumers and **re-registers `IJobOrchestrator`
  as a singleton bound to `IBus`**. Last-registration-wins, so **`AddEmaigratorWorkers` must be called
  *after* `AddInfrastructure`**.
- The Workers bus reads its broker from `ConnectionStrings:RabbitMq`; the API's own bus reads
  `Infrastructure:RabbitMqConnectionString` (different keys — see `deploy/docker-compose.yml`).

Composition roots: `EMaigrator.Workers/Program.cs`, `EMaigrator.Cli/Hosting/CliHostBuilder.cs`,
`EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`.

## Adding a connector

See **[`connectors/authoring-a-connector.md`](connectors/authoring-a-connector.md)** for the full recipe
(the skeleton, the `TryAddEnumerable` registration in all three composition roots, the secret/settings key
table, and the credential-free error-normalization rule).

## Tests & CI — what runs where

- **`*.Tests`** are pure unit tests (no Docker) — these run in CI. **`EMaigrator.Api.Tests` and the
  `*.IntegrationTests`** use **Testcontainers** and **require Docker Desktop running**; they are a local
  gate (CI does not run them, to keep the hosted runner fast and deterministic). Run the full suite
  locally with Docker up before opening a PR that touches the data/worker/API paths.
- The continuously-enforced guards are the **NetArchTest** dependency-rule tests in
  `EMaigrator.Core.Tests` (Core may reference only the BCL) and the **NuGet vulnerability gate**
  (`scripts/check-vulnerable.ps1`).
- Web gates: `npm run build`, `npm run test -- --run`, `npm run scan:bundle` (bundle secret-scan), and
  `npm run e2e` (Playwright vs the MSW mock) all run in CI.

## Reporting issues

- **Bugs / features:** open a [GitHub Issue](../../issues) using the templates.
- **Security:** follow [SECURITY.md](../SECURITY.md) — report privately, never in a public issue.
