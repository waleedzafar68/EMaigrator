# CLAUDE.md — EMaigrator

> Agent operating guide for this repo. Keep this file **lean** (< 300 lines, < 40k chars). It points at the source-of-truth docs; it does not duplicate them.

## What this is

EMaigrator is a non-destructive, idempotent, resumable **email-migration tool** (WorkMail ⇄ MS365 ⇄ Google for v1), shipped open-core: a free self-hostable OSS core + a separate proprietary hosted layer. Streaming pass-through — **message bodies are never persisted**.

## Document map (read before working)

| Doc | Owns |
|---|---|
| `DESIGN.md` | Architecture, data model, security, billing, stack, scope, decision log |
| `ARCHITECTURE.md` | Execution & parallelism (queue, workers, Redis token buckets) |
| `UX-Guide.md` | Operator flows, screens, states, copy |
| `FRONTEND-DESIGN.md` | Visual system (modern-technical, teal/slate, light+dark, SPA) |
| `docs/superpowers/plans/2026-06-01-emaigrator-CONTRACTS.md` | **FROZEN** signatures — the single source of truth |
| `docs/superpowers/plans/2026-06-01-emaigrator-00-INDEX.md` | The 10-plan set, dependency waves, conventions |
| `docs/CONTRIBUTING.md` | Build strictness, DI composition seams, CI scope, Windows ops — first-touch mechanics |
| `docs/connectors/authoring-a-connector.md` | The repeatable recipe for a new provider connector + secret/settings key table |
| `docs/KNOWN-ISSUES.md` | Tracked non-blocking defects (resume-completion race, retry backoff, Wave-F live wiring) |
| `design_handoff_emaigrator/` | Frontend visual reference prototype |

## The plan set

Implementation is **10 subsystem plans** (137 sub-tasks) in `docs/superpowers/plans/`. Each plan is independently testable, follows TDD, and ends with a **Security Verification** (`userGate`) + **Functional Verification** task. Native tasks (one per plan) track progress; per-plan bite-sized tasks live in each `<plan>.md` + co-located `<plan>.md.tasks.json`.

Execute with `superpowers-extended-cc:subagent-driven-development` (recommended) or `superpowers-extended-cc:executing-plans <plan-path>`. Respect the dependency waves below.

<!-- BUILD-STATUS:START — auto-maintained; see "Keeping this current" -->
## Build Status

_Last updated: 2026-06-04 — **Plan 10 (Frontend) COMPLETE — ALL 10 PLANS NOW ✅.** The Vite + React 19 + TS SPA is built and verified against the frozen CONTRACTS §6 wire shapes (MSW mock API + injectable fake SignalR): 15 sub-tasks via subagent-driven-development (implementer + spec + code-quality review each). `npm --prefix web run test -- --run` 81/81; `build` exit 0; Playwright happy-path E2E `1 passed`; `scan:bundle` OK. The security userGate was adversarially verified (5 controls proven to have teeth) and the functional gate (E2E) likewise (break→fail→revert). A real wizard state-staleness bug was found by the E2E and fixed (`WizardShell` re-fetches the draft per step). Live API/SignalR integration is the documented Wave-F follow-up (usage-data wiring, API-authoritative canBatch, cross-origin WS, per-row dashboard live updates). Prior: **Plan 09 (CLI) COMPLETE** — Tasks 12–15 finished on the working engine (Plan 08R). Secret-bundle unification (secrets stored as connector-shaped JSON: `{"password":…}` for IMAP basic, so connect-test/preflight/run all resolve against the real connector); in-process single-node worker wired into `CliHostBuilder` (`AddInfrastructure(registerBus:false)` + `AddEmaigratorWorkers`); `EfMigrationFactory`/`EfMigrationStateReader`/`EfMigrationResetter` + `SchemaMigratorHostedService`. CLI unit 51/51; CLI integration **7/7** via Testcontainers (preflight+run migrates 20, resume idempotent→25, security userGate, functional userGate). An adversarial-verify workflow (4 skeptics + independent reproduction) returned **GO / HIGH confidence**: no secret leak, no message duplication, no false-green gate. Non-blocking follow-up tracked: the engine resume-completion race in `EMaigrator.Workers` (a single `resume` of a finished migration can report Completed before the re-seeded items finish — delayed/misleading status, **no data loss or duplication**; resume is idempotent). **All 10 plans except Frontend (10) are now ✅.**_

| # | Plan | Sub-tasks | Status | Wave | Verified |
|---|---|---|---|---|---|
| 01 | Foundation | 8 | ✅ done | A | 2026-06-02 · `dotnet test src/EMaigrator.sln -c Release` (21 passed, 0 failed) |
| 02 | Core | 18 | ✅ done | B | 2026-06-02 · `dotnet test src/EMaigrator.sln -c Release` (115 passed, 0 failed; Core coverage line 1.00 / branch 0.99) |
| 03 | Infrastructure | 14 | ✅ done | C | 2026-06-02 · `dotnet test src/EMaigrator.sln -c Release` (153 passed, 0 failed; Infra integration 31 via Testcontainers, security + functional gates green) |
| 04 | Connector: IMAP | 11 | ✅ done | C | 2026-06-02 · `dotnet test src/EMaigrator.sln -c Release` (207 passed, 0 failed; IMAP unit 46 + integration 11 via GreenMail Testcontainers; security userGate + functional gates green) |
| 05 | Connector: Graph | 14 | ✅ done | C | 2026-06-02 · `dotnet test src/EMaigrator.sln -c Release` (289 passed, 2 skipped, 0 failed; Graph unit 82 via WireMock; security userGate + functional round-trip gates green; live-smoke + live-audit are opt-in skips) |
| 06 | Connector: Gmail | 14 | ✅ done | C | 2026-06-03 · `dotnet test src/EMaigrator.sln -c Release` (367 passed, 2 skipped, 0 failed; Gmail unit 79 via WireMock; security userGate + functional E2E gates green; paid-Workspace live testing deferred per DESIGN §17) |
| 07 | Workers | 13 | ✅ done | D | 2026-06-03 · `dotnet test src/EMaigrator.Workers.Tests -c Release` (27 passed) + `dotnet test src/EMaigrator.Workers.IntegrationTests -c Release` (10 passed: 4 E2E pipeline + 3 Redis-gate + 3 security; security userGate + functional gates green via Postgres/RabbitMQ/Redis/GreenMail Testcontainers) |
| 08 | API + 08R seams | 15+8 | ✅ done | E | 2026-06-04 · API REST/SignalR/Identity suite green (74) + **worker data-seams built via plan 08R** (8 tasks): `dotnet test src/EMaigrator.Workers.Tests -c Release` (36 passed) + `dotnet test src/EMaigrator.Workers.IntegrationTests -c Release` (17 passed, incl. real-seam functional E2E `Completed`/`MigratedCount==20` + security no-body-persistence gate, 0 sentinel across 26 cols/8 tables). Real migration runs end-to-end; terminal `MailboxMigration.Status` written. |
| 09 | CLI | 15 | ✅ done | E | 2026-06-04 · Tasks 1–15 complete. `dotnet test src/EMaigrator.Cli.Tests -c Release` (51 passed) + `dotnet test src/EMaigrator.Cli.IntegrationTests -c Release` (7 passed via Postgres/RabbitMQ/Redis/GreenMail Testcontainers: preflight+run E2E migrates 20, resume idempotency→25, security userGate [no plaintext-arg/echo, owner-only profile, secret-free json], functional userGate [new→connect test→preflight→run→status→report]); Release 0/0. Secret-bundle unification (connector-shaped JSON) + in-process worker (`AddEmaigratorWorkers`) + `EfMigrationFactory`/`EfMigrationStateReader`/`EfMigrationResetter` + `SchemaMigratorHostedService` wired into `CliHostBuilder`. Adversarial-verify workflow → GO/HIGH. Non-blocking follow-up: resume-completion race in Workers (delayed/misleading status; no data loss/dup). |
| 10 | Frontend | 15 | ✅ done | C→F | 2026-06-04 · `npm --prefix web run test -- --run` (81 passed, 31 files: unit + a11y + security) + `npm --prefix web run build` (tsc -b strict, exit 0) + `npm --prefix web run e2e` (Playwright happy-path **1 passed** vs MSW mock + fake SignalR) + `npm --prefix web run scan:bundle` (**bundle-scan OK**, exit 0). Security userGate **adversarially verified** — 5 controls each proven to have teeth (inject→fail→revert): no secret in storage, cookie auth (`credentials:include`, no token Authorization, no storage-reading SignalR accessTokenFactory), XSS-safe escaped rendering (no `dangerouslySetInnerHTML`), no secret-shaped content in dist bundle, CSP meta. a11y: vitest-axe gate (dashboard + wizard step, matcher adversarially proven), keyboard/focus, status icon+label. 15 commits one-per-task. Live API/SignalR integration = documented Wave-F follow-up (usage data, canBatch authority, cross-origin WS, per-row live updates). |

Status legend: ⬜ pending · 🔵 in progress · ✅ done (all sub-tasks + security & functional gates green) · ⚠️ blocked.
<!-- BUILD-STATUS:END -->

### Keeping this current (the auto-update provision)

**Standing instruction — every executing agent MUST follow this:** when a plan/phase changes state, update the `BUILD-STATUS` table above **in the same commit** as the work:
- On starting a plan → set its row to 🔵 in progress.
- On finishing a plan → set ✅ **only after** its Functional Verification AND Security Verification (`userGate`) tasks pass; put the date + the passing verify command in "Verified"; bump "_Last updated_".
- If blocked → ⚠️ with a one-line reason.

Only the region between the `BUILD-STATUS` markers changes; keep the file under 300 lines / 40k chars. To enforce this automatically, a `Stop` hook can re-check the table against native task state (ask the maintainer to wire it via `/update-config`).

## Dependency waves (parallelizable)

```
A: 01 foundation
B: 02 core
C: 03 infra · 04 imap · 05 graph · 06 gmail · 10 frontend (vs mock)   ← parallel
D: 07 workers           (needs 02,03,04,05)
E: 08 api · 09 cli      (need 02,03,07)        ← parallel
F: 10 frontend ↔ 08     (live API + SignalR integration)
```

Connectors (04/05/06) are mutually independent. Frontend (10) builds against the frozen REST/SignalR contract with a mock in Wave C, integrates live in Wave F. v1 release leads with WorkMail→MS365 (IMAP source + Graph dest).

## Non-negotiable conventions

1. **TDD within tasks** — red → green → refactor → commit, inside each task.
2. **Verify before done** — every task has a `**Verify:**` command; it must pass.
3. **Security + functional gates per plan** — the `userGate` Security Verification and the Functional Verification tasks are non-skippable; close them only with captured evidence, never a cheaper substitute.
4. **Bind to CONTRACTS** — never invent or alter a signature in the frozen contracts. A needed change is a coordination event: update CONTRACTS first, then every consumer.
5. **Dependency rule** — `EMaigrator.Core` references nothing; connectors/infrastructure depend only on Core abstractions; Api/Workers/Cli compose via DI. The NetArchTest suite in `EMaigrator.Core.Tests` enforces this (Core may reference only BCL assemblies).
6. **No body persistence** — message bodies/attachments transit memory only. Enforced structurally (`CanonicalMessage` has no body field — only a deferred `OpenContentAsync` stream) and by schema introspection in `EMaigrator.Infrastructure.IntegrationTests/Security/InfrastructureSecurityTests.cs` (forbidden-column Theory over `information_schema` + ciphertext canary). New persisted tables MUST be added to that Theory.
7. **Self-host/cloud parity** — engine deps must be container-friendly OSS (Postgres, RabbitMQ, Redis, OTel). No cloud-locked primitives in the core.
8. **Open-core boundary** — do NOT add hosted-only concerns (Stripe billing, multi-tenant orchestration beyond row-level filter, branded OAuth, AI fallback) to this OSS repo; they live in the separate private repo.
9. **Commits** — Conventional Commits, one per task, trailer:
   `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

## Patterns & gotchas (the non-obvious things that speed maintenance)

These bite repeatedly and aren't obvious from any single file. Fuller detail in `docs/CONTRIBUTING.md`.

- **Build is strict.** CPM (versionless refs → `src/Directory.Packages.props`), warnings-as-errors + latest-recommended analyzers (fix the code; only `*Tests` relax CA1707/CA1861), NU1902 audit-as-error, classic `.sln` required (not `.slnx`).
- **DI composition.** One MassTransit bus per process: `AddInfrastructure(config, registerBus:false)` **then** `AddEmaigratorWorkers` — order matters (last-wins singleton `IJobOrchestrator` bound to `IBus`, resolved from the *root* provider). `IPreflightAnalyzer`/`IErrorCatalog` are added by the host, not by `AddInfrastructure`.
- **Secret-bundle shape.** Secrets are stored as connector-shaped JSON (`{"password"|"accessToken"|"clientSecret"|"serviceAccountJson":…}`) deserialized into `SecretBundle.Values`. Wrong key ⇒ fakes pass, real run fails auth. → CONTRACTS §4 + connector guide §4.
- **Idempotency.** `Core/Idempotency/IdentityKey.cs` is the dedup key: normalized `Message-ID`, else SHA-256 over headers + the **decoded-body** fingerprint (NEVER raw transport bytes — servers rewrite in transit). Enforced by `ILedger.IsDoneAsync` + DB `UNIQUE(MailboxMigrationId, IdentityKey)`.
- **API tenancy is fail-open.** Inject the *scoped* `EmaigratorDbContext` in endpoints (its sentinel `CurrentTenantId` filter 404s cross-tenant ids); injecting `IDbContextFactory<EmaigratorDbContext>` yields an UNFILTERED context (`Guid.Empty` default) and silently bypasses tenancy. The SignalR hub can't use the filter (HttpContext null on WS) — it adds an explicit `j.TenantId == tenant` predicate; any new hub method must too. Three DbContexts share one DB (`__EFMigrationsHistory{,_Identity,_ApiSide}`).
- **Adding a connector** = one `IProviderPlugin` + `TryAddEnumerable`, registered in **all three** composition roots (Workers/Cli/Api). Recipe + invariants in `docs/connectors/authoring-a-connector.md`.
- **Frontend (`/web`).** Reach the API only through the typed wrappers in `api/migrations.ts` (over `apiFetch` — the single place cookie auth `credentials:include` + `ApiError` mapping live; never `fetch` from a component). Wizard steps read the authoritative migration from `WizardShell`'s Outlet context (re-fetched per route). Live SignalR via `MigrationsHubClient`/`useMigrationStream` (cookie auth, no `accessTokenFactory`).
- **CI ≠ the full gate.** `ci.yml` provisions no Docker, so `*.IntegrationTests` don't really run there; `e2e`/`scan:bundle`/lint/check-scripts are local/agent gates — run them with Docker up before calling a plan green.
- **Known issues** (resume-completion race, no-backoff retry, Wave-F live wiring) → `docs/KNOWN-ISSUES.md`.
- **Windows/agent ops.** Run `*-check.ps1` via the Bash tool (`pwsh -NoProfile -File`); worktree isolation is unreliable (space in path). → `docs/CONTRIBUTING.md`.

## Stack & key commands

Stack: .NET 10 / ASP.NET Core · EF Core + PostgreSQL · MassTransit + RabbitMQ · Redis · SignalR · MailKit / Microsoft.Graph / Google.Apis.Gmail · OpenTelemetry+Serilog · Vite + React 19 + TS + Tailwind + shadcn/ui. Tests: xUnit, FluentAssertions, NSubstitute, Testcontainers, WireMock.Net, GreenMail; Vitest + Testing Library + Playwright.

```bash
dotnet build src/EMaigrator.sln -c Release          # build
dotnet test  src/EMaigrator.sln -c Release          # all .NET tests (Docker needed for integration)
docker compose -f deploy/docker-compose.yml up       # Postgres + RabbitMQ + Redis (+ app)
npm --prefix web run test -- --run                   # frontend unit tests
npm --prefix web run e2e                             # Playwright E2E
dotnet list package --vulnerable --include-transitive # supply-chain audit (CI-gated)
```

Integration tests and `docker compose` require Docker Desktop running. Windows shell is PowerShell (the Bash tool is also available).

## Repo layout (target — created by Plan 01)

```
/src    EMaigrator.sln — Core, Connectors.{Imap,Graph,Gmail}, Infrastructure, Workers, Api, Cli + *.Tests
/web    Vite + React + TS SPA
/deploy docker-compose (Postgres + RabbitMQ + Redis) + Dockerfiles
/docs   superpowers/plans/ (the plan set + frozen contracts), CONTRIBUTING, KNOWN-ISSUES, connectors/ (authoring + testing)
```

This repo is private (`github.com/waleedzafar68/EMaigrator`). The hosted layer is a separate private repo consuming the OSS engine as a NuGet package.
