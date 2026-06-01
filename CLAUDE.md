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
| `design_handoff_emaigrator/` | Frontend visual reference prototype |

## The plan set

Implementation is **10 subsystem plans** (137 sub-tasks) in `docs/superpowers/plans/`. Each plan is independently testable, follows TDD, and ends with a **Security Verification** (`userGate`) + **Functional Verification** task. Native tasks (one per plan) track progress; per-plan bite-sized tasks live in each `<plan>.md` + co-located `<plan>.md.tasks.json`.

Execute with `superpowers-extended-cc:subagent-driven-development` (recommended) or `superpowers-extended-cc:executing-plans <plan-path>`. Respect the dependency waves below.

<!-- BUILD-STATUS:START — auto-maintained; see "Keeping this current" -->
## Build Status

_Last updated: 2026-06-02 — Plan 01 (Foundation) in progress._

| # | Plan | Sub-tasks | Status | Wave | Verified |
|---|---|---|---|---|---|
| 01 | Foundation | 8 | 🔵 in progress | A | — |
| 02 | Core | 18 | ⬜ pending | B | — |
| 03 | Infrastructure | 14 | ⬜ pending | C | — |
| 04 | Connector: IMAP | 11 | ⬜ pending | C | — |
| 05 | Connector: Graph | 14 | ⬜ pending | C | — |
| 06 | Connector: Gmail | 14 | ⬜ pending | C | — |
| 07 | Workers | 13 | ⬜ pending | D | — |
| 08 | API | 15 | ⬜ pending | E | — |
| 09 | CLI | 15 | ⬜ pending | E | — |
| 10 | Frontend | 15 | ⬜ pending | C→F | — |

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
5. **Dependency rule** — `EMaigrator.Core` references nothing; connectors/infrastructure depend only on Core abstractions; Api/Workers/Cli compose via DI. An architecture test enforces this.
6. **No body persistence** — message bodies/attachments transit memory only; tests assert zero body bytes hit Postgres/disk.
7. **Self-host/cloud parity** — engine deps must be container-friendly OSS (Postgres, RabbitMQ, Redis, OTel). No cloud-locked primitives in the core.
8. **Open-core boundary** — do NOT add hosted-only concerns (Stripe billing, multi-tenant orchestration beyond row-level filter, branded OAuth, AI fallback) to this OSS repo; they live in the separate private repo.
9. **Commits** — Conventional Commits, one per task, trailer:
   `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

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
/docs   superpowers/plans/ (the plan set + frozen contracts), self-host & connector-authoring guides
```

This repo is private (`github.com/waleedzafar68/EMaigrator`). The hosted layer is a separate private repo consuming the OSS engine as a NuGet package.
