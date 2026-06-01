# EMaigrator v1 — Implementation Plan Set (Master Index)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement these plans task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the EMaigrator v1 OSS core — a non-destructive, idempotent, resumable email-migration tool covering the WorkMail ⇄ MS365 ⇄ Google triangle — as a set of independently-testable subsystem plans that multiple subagents can implement in parallel.

**Architecture:** Hub-and-spoke canonical model (`source → canonical → destination`); durable queue (MassTransit/RabbitMQ) + idempotent stateless workers + Postgres ledger-as-state; Redis distributed token buckets for per-account rate limiting + SignalR backplane; ASP.NET Core REST API + SignalR; Vite/React SPA. Streaming pass-through — message bodies never persisted. Full detail in `DESIGN.md`, `ARCHITECTURE.md`, `UX-Guide.md`, `FRONTEND-DESIGN.md`.

**Tech Stack:** C#/.NET 10 (LTS), ASP.NET Core, EF Core + PostgreSQL, MassTransit + RabbitMQ, StackExchange.Redis, SignalR, MailKit (IMAP), Microsoft.Graph, Google.Apis.Gmail, OpenTelemetry/Serilog; Vite + React 19 + TypeScript + Tailwind + shadcn/ui. Tests: xUnit, FluentAssertions, NSubstitute, Testcontainers, WireMock.Net, GreenMail; Vitest + Testing Library + Playwright.

---

## Frozen Contracts — READ FIRST

**Every plan binds to [`2026-06-01-emaigrator-CONTRACTS.md`](./2026-06-01-emaigrator-CONTRACTS.md).** It defines the canonical model, all DI interfaces, MassTransit message contracts, EF entity shapes, REST/SignalR contracts, and config option classes — at the signature level. **No plan may invent or alter a signature defined there.** If a plan needs a contract change, that is a coordination event: update CONTRACTS first, then every consuming plan. This single source of truth is what makes parallel work safe.

---

## The Plans

| # | Plan file | Subsystem | Produces |
|---|---|---|---|
| 01 | `…-01-foundation.md` | Repo & solution scaffolding | `/src` .NET solution skeleton, all project stubs + references honoring the dependency rule, `/web` Vite app stub, `/deploy` docker-compose (Postgres+RabbitMQ+Redis), CI (build+test+coverage+`dotnet list package --vulnerable`), shared test harness |
| 02 | `…-02-core.md` | `EMaigrator.Core` | Canonical model, all DI interfaces (from CONTRACTS), identity-key/hash, folder sanitizer/flattener, error catalog + rule matching, pre-flight analyzer/planner, remediation taxonomy. **Pure logic, ~100% unit-tested.** |
| 03 | `…-03-infrastructure.md` | `EMaigrator.Infrastructure` | EF Core/Postgres entities + migrations, ledger repository, `ISecretStore` (local-key + KMS envelope), Redis token-bucket (atomic Lua) + adaptive backoff, MassTransit/RabbitMQ wiring, OTel/Serilog, health checks |
| 04 | `…-04-connector-imap.md` | `EMaigrator.Connectors.Imap` | IMAP `ISourceProvider`+`IDestinationProvider` (MailKit), WorkMail region presets, constraints, XOAUTH2 + basic auth, test-connection |
| 05 | `…-05-connector-graph.md` | `EMaigrator.Connectors.Graph` | Microsoft Graph source+dest, BYO-OAuth (app perms + admin consent), constraints, test-connection |
| 06 | `…-06-connector-gmail.md` | `EMaigrator.Connectors.Gmail` | Gmail API source+dest, BYO service-account + DWD, labels↔folders, constraints, test-connection |
| 07 | `…-07-workers.md` | `EMaigrator.Workers` | Orchestration consumers, fan-out (Migration→Folder→Batch→Message), streaming copy, checkpoint/resume, DLQ→needs-decision, pause/resume/cancel, rate-limiter integration, E2E Testcontainers pipeline |
| 08 | `…-08-api.md` | `EMaigrator.Api` | REST endpoints (migrations/drafts/connect-test/scope/preflight/run/results/export), SignalR hub + Redis backplane, ASP.NET Identity + tenancy, email notifications, health |
| 09 | `…-09-cli.md` | `EMaigrator.Cli` | CLI commands wrapping the engine (new/connect-test/preflight/run/resume/status), config file, exit codes |
| 10 | `…-10-frontend.md` | `/web` (Vite React SPA) | Design tokens (FRONTEND-DESIGN.md), API + SignalR clients, dashboard, 6-step wizard, global/error/reconnecting states, theming, a11y; references `design_handoff_emaigrator/` prototype |

---

## Dependency Graph & Parallelization Waves

```
Wave A (serial):     01 foundation
                          │
Wave B (serial):     02 core  ───────────────┐  (defines concrete contracts)
                          │                   │
Wave C (parallel):   03 infra   04 imap   05 graph   06 gmail   10 frontend*
                          └────────┴──────────┴──────────┘          │
Wave D (serial):     07 workers (needs 02,03, ≥1 connector)         │ *frontend can
                          │                                          │  start in Wave C
Wave E (parallel):   08 api (needs 02,03,07)   09 cli (needs 02,03,07,connectors)
                          │                                          │
Wave F:              10 frontend integration (binds to 08's live API + SignalR)
```

- **`blockedBy` (cross-plan):** 02→01; {03,04,05,06}→02; 07→{02,03,04 (≥ IMAP source + Graph dest for the wedge)}; 08→{02,03,07}; 09→{02,03,07, connectors}; 10→01 (scaffold) for build-out, →08 for live integration.
- **Frontend (10)** develops against the **frozen REST/SignalR contract** with a mock server in Wave C, then integrates against the live API in Wave F — so it parallelizes with the backend.
- **Connectors (04/05/06)** are mutually independent (all bind only to Core interfaces) → three subagents in parallel.
- **v1 release phasing** (`DESIGN.md §4.1`): IMAP-source + Graph-dest (WorkMail→MS365) is the critical path; Gmail (06) and reverse directions can lag without blocking first release.

---

## Conventions Every Plan Follows (non-negotiable)

1. **TDD within tasks.** Every code task is red → green → refactor *inside* the task. Write the failing test, run it (show expected FAIL), implement minimally, run (show PASS), commit. Never a "write tests later" task.
2. **Functional verification per task.** Every task has a `**Verify:**` line with an exact command and expected output. A task is not done until its verify command passes.
3. **Security verification per plan.** Every plan ends with a dedicated **Security Verification task** (`userGate: true`) appropriate to its surface — see the per-plan security checklist below. Plus a **Functional Verification / acceptance task** proving the subsystem's headline behavior end-to-end.
4. **Commit cadence.** One commit per task (Conventional Commits). Co-author trailer on every commit:
   `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
5. **No placeholders.** Complete code in every code step; exact file paths; exact commands. (Skill rule.)
6. **Dependency rule (`DESIGN.md §15`).** `Core` references nothing; Connectors/Infrastructure depend only on `Core` abstractions; Api/Workers/Cli compose via DI. An architecture test enforces this (Task in Plan 02).
7. **No body persistence (`DESIGN.md §10`).** Message bodies/attachments transit memory only. A test asserts no body content reaches Postgres/disk (Plans 03, 07).

### Per-plan security verification focus

| Plan | Security verification must prove |
|---|---|
| 01 foundation | Supply-chain: `dotnet list package --vulnerable` gate fails the build on any advisory; secrets hygiene: `.gitignore` covers secret patterns, `git ls-files` shows zero tracked secret files, `.env.example` holds only placeholders |
| 02 core | Identity hash is content-fingerprint only (no secret leakage); error catalog never echoes credentials in diagnoses |
| 03 infra | Credentials encrypted at rest (DB breach → ciphertext); secrets decrypt only transiently, never logged; ledger/log tables hold **no** message bodies/sender/recipient; 30-day purge job; credential purge on terminal state |
| 04/05/06 connectors | Credentials never written to logs/exceptions; TLS enforced; OAuth scopes least-privilege; test-connection cannot exfiltrate to arbitrary host |
| 07 workers | Streaming pass-through asserts zero body bytes persisted; DLQ payloads carry no content; rate-limiter prevents provider lockout |
| 08 api | AuthN required on all non-public routes; tenant isolation (row-level filter) enforced by test; no secrets in API responses; input validation; anti-CSRF/CORS; rate-limit on auth endpoints; security headers |
| 09 cli | Credentials read from secure input/env, never echoed; config file perms |
| 10 frontend | No secrets in localStorage/bundle; XSS-safe rendering of mail subjects/folder names; auth token handling; CSP |

---

## Definition of Done (whole set)

- All plan tasks complete; each subsystem's functional + security verification tasks pass.
- `docker compose up` brings up app + Postgres + RabbitMQ + Redis; CLI and API both run a real WorkMail→MS365 (recorded-fixture) migration green.
- Coverage: deterministic core ~100%; CI green including `--vulnerable` audit.
- Frontend wizard drives a full migration against the live API.
