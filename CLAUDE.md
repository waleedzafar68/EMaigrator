# CLAUDE.md — EMaigrator

> Engineering guide for contributors and AI assistants. Captures the intent, design, stack, and the
> non-obvious patterns that make features and fixes go faster. Keep it **lean** (< 300 lines); it points
> at the source-of-truth docs rather than duplicating them.

## What this is

EMaigrator is a non-destructive, idempotent, resumable **email-migration tool** (WorkMail ⇄ MS365 ⇄
Google for v1). It is **streaming pass-through — message bodies and attachments are never persisted**;
only minimal metadata (subject, date, folder) is stored, briefly. v1 leads with **WorkMail → MS365**
(IMAP source + Microsoft Graph destination).

It ships **open-core**: this repository is the free, self-hostable engine (Apache-2.0). Hosted-only
concerns — billing/quotas, multi-tenant orchestration beyond a row-level filter, branded OAuth, AI
fallback — live in a separate layer and must **not** land here (see convention 8).

## Document map (read before working)

| Doc | Owns |
|---|---|
| `DESIGN.md` | Architecture, data model, security, stack, scope, decision log |
| `ARCHITECTURE.md` | Execution & parallelism (queue, workers, Redis token buckets) |
| `UX-Guide.md` | Operator flows, screens, states, copy |
| `FRONTEND-DESIGN.md` | Visual system (modern-technical, teal/slate, light+dark, SPA) |
| `docs/CONTRIBUTING.md` | Build strictness, DI composition seams, CI scope, Windows ops — first-touch mechanics |
| `docs/connectors/authoring-a-connector.md` | The repeatable recipe for a new provider connector + secret/settings key table |
| `docs/KNOWN-ISSUES.md` | Tracked non-blocking defects (resume-completion race, retry backoff) |

## Architecture in one paragraph

A migration is a **job** of mailbox pairs. The API enqueues work onto **RabbitMQ** (via MassTransit);
**workers** stream each message source→destination through `CanonicalMessage` (a metadata envelope plus a
deferred `OpenContentAsync` content stream — there is no body field, by design). Per-tenant/-provider rate
limits use **Redis** token buckets. State, ledger, and audit live in **PostgreSQL** (three EF Core
DbContexts share one database). The **SPA** (`/web`) talks to the API over REST + a **SignalR** hub for
live progress. In Docker, an nginx `web` service serves the built SPA and reverse-proxies `/api` + `/hubs`
to the API, so the browser sees a single same-origin app.

## Non-negotiable conventions

1. **TDD** — red → green → refactor → commit. Keep changes small and verifiable.
2. **Verify before done** — run the relevant build/test command and make it pass before calling something complete.
3. **Security + functional tests are non-skippable** — the per-subsystem security and functional test
   suites (e.g. the no-body-persistence and cross-tenant gates) must stay green; never substitute a cheaper check.
4. **Shared contracts are coordination events** — the REST/SignalR/message shapes and cross-module
   signatures in code are the source of truth. Changing one means updating **every** consumer in the same change.
5. **Dependency rule** — `EMaigrator.Core` references nothing but the BCL; connectors/infrastructure depend
   only on Core abstractions; Api/Workers/Cli compose via DI. Enforced by the NetArchTest suite in `EMaigrator.Core.Tests`.
6. **No body persistence** — bodies/attachments transit memory only. Enforced structurally (`CanonicalMessage`
   has no body field — only `OpenContentAsync`) and by schema introspection in
   `EMaigrator.Infrastructure.IntegrationTests/Security/InfrastructureSecurityTests.cs` (forbidden-column
   Theory over `information_schema` + a ciphertext canary). **Any new persisted table MUST be added to that Theory.**
7. **Self-host/cloud parity** — engine deps must be container-friendly OSS (Postgres, RabbitMQ, Redis, OTel).
   No cloud-locked primitives in the core.
8. **Open-core boundary** — do NOT add hosted-only concerns (Stripe billing, multi-tenant orchestration
   beyond a row-level filter, branded OAuth, AI fallback) to this repo.
9. **Commits** — Conventional Commits, one logical change per commit, trailer:
   `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

## Patterns & gotchas (the non-obvious things that speed maintenance)

These bite repeatedly and aren't obvious from any single file. Fuller detail in `docs/CONTRIBUTING.md`.

- **Build is strict.** CPM (versionless refs → `src/Directory.Packages.props`), warnings-as-errors +
  latest-recommended analyzers (fix the code; only `*Tests` relax CA1707/CA1861), NU1902 audit-as-error,
  classic `.sln` required (not `.slnx`).
- **DI composition.** One MassTransit bus per process: `AddInfrastructure(config, registerBus:false)`
  **then** `AddEmaigratorWorkers` — order matters (last-wins singleton `IJobOrchestrator` bound to `IBus`,
  resolved from the *root* provider). `IPreflightAnalyzer`/`IErrorCatalog` are added by the host, not by `AddInfrastructure`.
- **Secret-bundle shape.** Secrets are stored as connector-shaped JSON
  (`{"password"|"accessToken"|"clientSecret"|"serviceAccountJson":…}`) deserialized into `SecretBundle.Values`.
  Wrong key ⇒ fakes pass, real run fails auth. → connector guide §4.
- **Idempotency.** `Core/Idempotency/IdentityKey.cs` is the dedup key: normalized `Message-ID`, else SHA-256
  over headers + the **decoded-body** fingerprint (NEVER raw transport bytes — servers rewrite in transit).
  Enforced by `ILedger.IsDoneAsync` + DB `UNIQUE(MailboxMigrationId, IdentityKey)`.
- **Reconcile is Graph/Exchange-only.** The optional `IReconcilableDestination` capability diffs the source
  against the **live** destination (metadata only) and copies-missing / backfills-missing-attachments /
  skips-complete — never duplicating. Gmail/IMAP deliberately don't implement it.
- **API tenancy is fail-open.** Inject the *scoped* `EmaigratorDbContext` in endpoints (its sentinel
  `CurrentTenantId` filter 404s cross-tenant ids); injecting `IDbContextFactory<EmaigratorDbContext>` yields
  an UNFILTERED context (`Guid.Empty`) and silently bypasses tenancy. The SignalR hub can't use the filter
  (HttpContext null on WS) — it adds an explicit `j.TenantId == tenant` predicate; any new hub method must too.
  Three DbContexts share one DB (`__EFMigrationsHistory{,_Identity,_ApiSide}`).
- **Adding a connector** = one `IProviderPlugin` + `TryAddEnumerable`, registered in **all three**
  composition roots (Workers/Cli/Api). Recipe + invariants in `docs/connectors/authoring-a-connector.md`.
- **Frontend (`/web`).** Reach the API only through the typed wrappers in `api/migrations.ts` (over
  `apiFetch` — the single place cookie auth `credentials:include` + `ApiError` mapping live; never `fetch`
  from a component). Wizard steps read the authoritative migration from `WizardShell`'s Outlet context
  (re-fetched per route). Live SignalR via `MigrationsHubClient`/`useMigrationStream` (cookie auth, no `accessTokenFactory`).
- **CI ≠ the full gate.** `*.IntegrationTests` need Docker and `e2e`/`scan:bundle` need browsers/build; run
  them locally with Docker up before trusting a green build. (See `docs/KNOWN-ISSUES.md` for the current CI gaps.)
- **Windows/agent ops.** Run `*-check.ps1` via the Bash tool (`pwsh -NoProfile -File`); worktree isolation
  is unreliable when the repo path contains spaces. → `docs/CONTRIBUTING.md`.

## Stack & key commands

Stack: .NET 10 / ASP.NET Core · EF Core + PostgreSQL · MassTransit + RabbitMQ · Redis · SignalR ·
MailKit / Microsoft.Graph / Google.Apis.Gmail · OpenTelemetry + Serilog · Vite + React 19 + TS + Tailwind +
shadcn/ui. Tests: xUnit, FluentAssertions, NSubstitute, Testcontainers, WireMock.Net, GreenMail; Vitest +
Testing Library + Playwright.

```bash
dotnet build src/EMaigrator.sln -c Release           # build
dotnet test  src/EMaigrator.sln -c Release           # all .NET tests (Docker needed for integration)
docker compose -f deploy/docker-compose.yml up -d     # full stack + web UI at http://localhost:3000
npm --prefix web run test -- --run                   # frontend unit tests
npm --prefix web run e2e                             # Playwright E2E
pwsh -NoProfile -File scripts/check-vulnerable.ps1   # .NET supply-chain audit
```

Integration tests and `docker compose` require Docker Desktop running. Windows shell is PowerShell (the
Bash tool is also available). The compose file is fully env-parameterized (ports, images, secrets, restart
policy) — if host ports clash with other local stacks, set the matching `*_PORT` vars in a gitignored
`deploy/.env` (no override file needed); internal service-to-service traffic uses container DNS and is unaffected.

## Repo layout

```
/src    EMaigrator.sln — Core, Connectors.{Imap,Graph,Gmail}, Infrastructure, Workers, Api, Cli + *.Tests
/web    Vite + React + TS SPA
/deploy docker-compose (Postgres + RabbitMQ + Redis) + Dockerfiles + nginx config
/docs   CONTRIBUTING, KNOWN-ISSUES, connectors/ (authoring + testing)
```
