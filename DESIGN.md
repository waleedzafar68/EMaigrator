# EMaigrator — Software Design Document

> **Status:** Design-locked architecture, ready for implementation. Open items are flagged in [§19](#19-open-items--tbd).
> **Audience:** Implementing agents and engineers. Decisions here are intentional; the rationale is recorded in [§20](#20-decision-log).
> **Last updated:** 2026-06-01

---

## 1. Overview

EMaigrator is an **email migration tool** that copies a mailbox from a source provider to a destination provider — preserving folder structure, flags, and message dates — **idempotently** (safe to re-run, no duplicates) and **non-destructively** (never deletes from source).

It is inspired by `aw2ms365` (a CLI WorkMail→MS365 tool) but differentiates on three axes:
1. **Multiple auth options** per provider, not a single hard-coded path.
2. **A clean wizard UI** usable by non-technical operators — not CLI-only.
3. **An error-resolution engine** that detects problems *before* migrating and proposes fixes, instead of dumping the operator into a failure (the original tool forced manual deletion of nested folders).

The immediate commercial thesis: **AWS WorkMail is being discontinued**, creating a time-boxed migration land-grab.

---

## 2. Personas

EMaigrator serves a **skill spread**, reconciled in one UI via progressive disclosure (see [§13](#13-frontend--ux)):

| Persona | Scale | Needs |
|---|---|---|
| **MSP / IT consultant / freelancer** | Hundreds of mailboxes, recurring | Batch mapping, CSV import, filters, overrides, BYO OAuth apps |
| **Self-serve individual** ("the old man") | One mailbox, one-time | Dead-simple linear path, IMAP credentials, walk-away unattended runs |

The **mailbox owners** whose email is moved are *not* users — they only supply consent/credentials.

---

## 3. Business & Open-Core Model

Three revenue layers over one engine:

1. **OSS core** (MIT/Apache) — a *complete, free, self-hostable* migration tool. Drives trust + SEO funnel (rank for "AWS WorkMail migration").
2. **Hosted SaaS** (proprietary) — removes operator setup pain; the paid product.
3. **Concierge / done-for-you** — sell the labor; highest margin.

### Open-core boundary (enforced physically by repo split — see [§15](#15-repository--solution-structure))

**Free OSS core:**
- Migration engine (canonical model, all v1 connectors, error catalog, pre-flight, idempotency ledger, queue/workers)
- CLI, REST API, Vite React app
- ASP.NET Core Identity auth (single-org / multi-user)
- OpenTelemetry instrumentation, 4-container docker-compose (app + Postgres + RabbitMQ + Redis)

**Hosted (closed, monetized):**
- Multi-tenant orchestration + org/workspace model
- Billing (Stripe)
- **Branded one-click OAuth** (pre-registered Google/MS apps + CASA — v2)
- Managed AI-fallback keys, autoscaling, support, concierge
- Social login (Google/Microsoft) layered on Identity

---

## 4. v1 Scope

**The bidirectional triangle: WorkMail ⇄ MS365 ⇄ Google ⇄ WorkMail.**

Six connector capabilities composing through the canonical hub:

| Connector | Source | Destination | Transport |
|---|---|---|---|
| IMAP | ✅ (WorkMail) | ✅ (generic — reused for v2 long tail) | IMAP |
| Graph | ✅ (MS365) | ✅ (MS365) | Microsoft Graph API |
| Gmail | ✅ (Google) | ✅ (Google) | Gmail API |

**WorkMail is IMAP-only** (no native API), so the wedge path is IMAP-source → Graph/Gmail-dest. Building the generic IMAP *destination* is not wasted — it is the foundation for the v2 long tail.

**Roadmap:**
- **v1** — the triangle (above).
- **v1.1** — evergreen reinforcement (MS365 ⇄ Google is permanent demand, independent of WorkMail's deadline).
- **v2** — IMAP long tail: Zoho, Hostinger, Migadu, mail.eu, Intermedia (cheap — mostly auth + constraints declarations on the existing IMAP transport).
- **v2** — branded one-click OAuth (after Google CASA + Microsoft publisher verification).

### 4.1 Timeline & Release Phasing

- **AWS WorkMail is officially decommissioning — unavailable after 2027-03-31.**
- **Engineering:** ~10 months of runway from mid-2026 → the full triangle is comfortably buildable; **scope is not deadline-constrained.**
- **Demand ≠ deadline.** WorkMail orgs migrate in the months *leading up* to the shutdown, not the final week — demand forms through H2 2026 and peaks ~Q4 2026–Q1 2027.
- **Therefore: phase the public release** (engineering target stays the full triangle):
  1. **WorkMail → MS365** first — target **~Q3/Q4 2026**. Highest-demand refugee path (most WorkMail orgs flee to Microsoft). Starts revenue early and **harvests real WorkMail-migration error data** to seed the error catalog (the differentiator's fuel).
  2. **WorkMail → Google** next.
  3. **MS365 ⇄ Google** (the evergreen pairs) to complete the triangle.

---

## 5. Architecture Overview

**Hub-and-spoke**, never N×M direct connectors. Every migration is `source → canonical model → destination`.

```
                         ┌─────────────────────────────┐
   Source provider  ──►  │     CANONICAL MAIL MODEL     │  ──►  Destination provider
   (ISourceProvider)     │  Message / Folder / Flags /  │       (IDestinationProvider)
                         │  InternalDate / Attachment   │
                         └─────────────────────────────┘
                                      ▲
                                      │ streaming pass-through
                                      │ (bodies transit memory, never persisted)
                                      │
   ┌──────────┐   enqueue   ┌──────────────┐   checkpoint   ┌──────────────────┐
   │   API    │ ──────────► │  RabbitMQ    │ ─────────────► │  Idempotency      │
   │ +SignalR │             │ (MassTransit)│                │  Ledger (Postgres)│
   └──────────┘             └──────┬───────┘                └──────────────────┘
        ▲                          │                                 ▲
        │ live progress            ▼ consume                         │ resume = re-enqueue
        │                   ┌──────────────┐                         │ not-done items
        └───────────────────│  Workers     │─────────────────────────┘
                            └──────────────┘
```

Adding a provider = one plugin assembly, not 8 connectors.

---

## 6. Migration Engine

### Transport strategy
- **Native-first** (Microsoft Graph, Gmail API) — more reliable, better rate-limit behavior, richer modeling of provider-specific concepts (Gmail labels, MS365 categories).
- **IMAP fallback** where native is unavailable (WorkMail, all v2 providers) or where native fails.
- Each provider plugin declares which transports + auth methods it supports.

### Streaming pass-through (no body persistence)
- A worker reads a message from source and writes it to destination **in-flight**.
- **Message bodies and attachments are never written to disk** — they transit worker memory transiently.
- Enables a truthful **"we never store your email"** claim (critical for EU/privacy segment) and slashes GDPR/breach surface. No object storage required.

### Idempotency & deduplication
- The **idempotency ledger** (Postgres) is the single source of truth for migration state.
- **Identity key** = RFC `Message-ID` header (primary). For the malformed/missing-ID long tail, a **composite hash over normalized stable fields**: `From | To | Subject | Date | SHA(decoded body)`.
- **NEVER hash raw message bytes** — servers rewrite messages in transit (Received headers, CRLF↔LF, MIME re-encoding), so raw-byte hashes produce false non-matches and duplicates.
- Hash is a **content fingerprint, not a security control** — SHA-256 is sufficient (SHA-512 acceptable).
- **Dedup scope (honest):** perfect for resume/re-run of our own migration. For a *non-empty* destination, match on `Message-ID` where possible; otherwise the product **recommends migrating into a fresh destination** for guaranteed no-dupes.
- **Resume = scan ledger for not-done items, re-enqueue.** Checkpoint per message (or small batch).

---

## 7. Error-Resolution Engine & Pre-flight

The product's primary differentiator.

### Core: deterministic rule catalog
- Data-driven mapping: `(provider, error-signature) → { diagnosis, suggestion, remediation, severity }`.
- Deterministic → **unit-testable** (satisfies the TDD/coverage requirement); works **offline / self-hosted** (no LLM key); covers the finite, recurring set of real migration failures.
- **Community-extensible** — new error→fix rules are OSS contributions.

### AI fallback (optional, hosted-configurable)
- For errors **not** in the catalog, a **cheap model** (Kimi / Haiku) generates a plain-language diagnosis + suggested action.
- **Never auto-fixes.** Output flows into the same "inform the user, user decides" path.
- Never required; self-hosters may supply their own key.

### Pre-flight scan (first-class, required)
- Each provider plugin **declares its constraints**: max folder depth, path-length limit, illegal name characters, message/attachment size caps, etc.
- A **read-only scan** inspects the source tree against the *destination's* constraints and produces a **remediation plan**: each issue shows the problem + a **recommended resolution** (e.g., "Folder /A/B/C/D/E exceeds Outlook's depth → flatten to /A-B-C-D-E").
- Pre-flight also performs the **billing quota check** ([§14](#14-billing-hosted)) and serves as the **plan-approval gate**. One screen, three jobs.

### Remediation model (no silent defaults)
- **Transient/operational** (429 throttle, dropped connection, timeout): handled **automatically** with backoff respecting `Retry-After`. Logged, *not* a user decision.
- **Structural/semantic** (flatten/rename folders, skip oversized/malformed message, merge folders): **decided by the user**, **batched up front at pre-flight**. Recommendation is *shown and selected* but **nothing applies until the user explicitly approves the plan** (opt-in, visible — not silent opt-out).
- The old man clicks **"Approve plan"** once; the MSP adjusts individual choices first.
- Mid-run surprises that couldn't be predicted go to a **post-run "needs decision" queue** (never a blocking pop-up); the user resolves and re-runs (safe — idempotent).

---

## 8. Orchestration & Execution

- **Durable queue + idempotent stateless workers + ledger-as-state.** Because every message-copy is idempotent, heavyweight durable-execution engines are unnecessary.
- **MassTransit** over **RabbitMQ** — transport-swappable (RabbitMQ for self-host; managed RabbitMQ or Azure Service Bus / SQS for hosted, by config). Behind an `IJobOrchestrator` interface.
- **Graduate to Temporal only if scale ever demands it** (kept behind the interface; almost certainly not needed).
- **Work granularity:** a *job* = one source→dest mailbox pair → fan out to **per-folder** work items → process messages in **small batches**.
- **Parallelism:** high across mailboxes, moderate across folders, batched within a folder.
- **Rate-limit handling (non-negotiable):** a **distributed per-`(provider, account)` token bucket in Redis** with adaptive backoff honoring `Retry-After`, plus per-tenant concurrency caps. This — not raw speed — is what makes migrations finish. See `ARCHITECTURE.md §4` for the full mechanism.
- **Redis** serves double duty: the distributed rate-limit buckets **and** the **SignalR backplane** (so progress events fan out across horizontally-scaled API instances). Open-source + container-friendly → preserves the parity principle.
- **Dead-letter queue** (MassTransit) for poison messages → surfaces in the post-run "needs decision" queue; one bad message never wedges a job.
- **Crash/deploy/restart:** workers return, scan the ledger, re-enqueue incomplete items.

---

## 9. Data Model & Persistence

**PostgreSQL** as the single relational store (OSS, no licensing friction for self-hosters, runs everywhere, JSONB for flexible metadata). Chosen over SQL Server specifically for open-core friendliness.

Core entities (sketch — refine during implementation):

- **Job** — `id`, `tenant_id`, source/dest provider + connection refs, scope (single/batch), status (`Queued|PreFlight|AwaitingApproval|Running|Paused|Completed|Failed|Cancelled`), timestamps.
- **MailboxMigration** — one source→dest mailbox pair within a Job. The **billing unit** ([§14](#14-billing-hosted)).
- **FolderTask** — per-folder work unit + status.
- **LedgerEntry** (idempotency) — `mailbox_migration_id`, `identity_key` (Message-ID or composite hash), source→dest folder mapping, status, error code, timestamps. **No bodies. No subjects unless logging enabled.**
- **MigrationLog** — `subject` (toggleable), `date`, `source_folder`, `dest_folder`, `status`, `error_code`. **Encrypted at rest. 30-day auto-purge.** *No sender/recipient.*
- **Credential** — encrypted blob (see [§10](#10-security--data-handling)). **Purged the instant the job is terminal.**
- **Tenant / Org / User** — Identity-backed; tenancy via row-level `tenant_id` + EF Core global query filters (hosted).

---

## 10. Security & Data Handling

### What we store / don't store
- ❌ **Never:** message bodies, attachments, inline content.
- ✅ **Logs (metadata):** subject + date + folder + status + error code. **No sender/recipient.** Subject is toggleable off per-job ("privacy mode" → folder + date + hash only). Encrypted at rest. **30-day auto-purge** + manual "Delete now."
- ✅ **Idempotency ledger:** identity hashes + folder mappings + status.

> **Privacy notice must be specific** (subjects may contain PII): *"We never store message bodies or attachments. We retain migration metadata — folder names, dates, subjects, status — encrypted, for up to 30 days, then auto-delete."* No PII scrubber on subjects (probabilistic, leaky, unkeepable promise) — use the **deterministic on/off toggle** instead.

### Credentials (the sharpest surface)
- In play: IMAP passwords/app-passwords, Microsoft OAuth client secrets, Google service-account key JSON, derived access/refresh tokens.
- **Envelope encryption via KMS:** per-tenant data key wrapped by a managed master key (Azure Key Vault / AWS KMS / equivalent). DB breach alone yields ciphertext.
- Behind a DI'd **`ISecretStore`** — `KmsEnvelopeSecretStore` (hosted) / local-key or Vault impl (self-host).
- **Lifecycle (distinct from logs):** credentials persist encrypted only while a job is runnable/resumable, and are **purged the instant the job reaches a terminal state**. Decrypted **only transiently in worker memory**, never logged, scrubbed after use.
- **Trust posture:** provider-managed keys → **not zero-knowledge** (we *could* decrypt). Customer-held-key mode is deferred (kills unattended resume). **Disclosed in bold at cloud signup.**

---

## 11. Authentication

Two distinct concerns — keep them separate:

### A. Provider auth (access to mailboxes being migrated)
v1 supports, per what each provider allows:
1. **IMAP basic / app-password** — WorkMail, all v2 providers, universal fallback.
2. **Bring-Your-Own OAuth app** — the operator follows guided in-app instructions to create their *own* Azure App Registration / Google service-account-with-domain-wide-delegation and pastes credentials. **Consequence: EMaigrator operates no shared branded OAuth app in v1 → Google CASA + Microsoft publisher verification do NOT apply to us.**
3. **Deferred to v2:** branded one-click delegated OAuth (triggers CASA — ~$1,800/tier + annual; defer until there are paying users).

> Note: basic-auth IMAP is dead on Gmail/MS365 themselves — IMAP there still requires OAuth (XOAUTH2). The IMAP-password shortcut only applies when the *source* is WorkMail/Hostinger/Zoho/etc.

### B. App auth (operator logging into EMaigrator)
- **ASP.NET Core Identity** (OSS, self-hostable — parity principle). Social login (Google/Microsoft) layered on in hosted only.

---

## 12. Observability

Reliability instrumentation is a **first-class product feature** ("reliability *is* the differentiator").

- **OpenTelemetry everywhere** (logs via Serilog→OTLP, metrics, traces). **Vendor-neutral by design:** self-host points at Grafana/Loki/Tempo/Prometheus; hosted points at its chosen backend. Same instrumentation, different sink (parity principle).
- **Migration-specific signals:** throughput (msgs/sec), error rate by provider + error-code, queue depth, worker saturation, rate-limit/429 hits, job duration/ETA, DLQ growth. (These also feed the live progress UI.)
- **Health checks** (ASP.NET Core) for Postgres, RabbitMQ, provider reachability.
- **SLOs + alerts (light layer):** job success rate, message-copy p95 latency, queue-drain time; alert on worker death, queue backlog, provider error-rate spikes, DLQ growth.

---

## 13. Frontend & UX

- **App = Vite + React SPA** (TypeScript), served as static assets, a pure client of the C# API. *Not* Next.js — a C# backend + Next SSR is two server runtimes for zero gain here.
- **API = ASP.NET Core REST**; **live progress = SignalR** (WebSocket + transport negotiation + reconnection — important for hours-long runs the operator leaves open).
- **Marketing + docs site = separate & later** (Astro/Next for SEO); never conflated with the app or its architecture.

### The wizard (one adaptive flow, progressive disclosure)

**Home = Migrations dashboard** (job list + live status + "New Migration"). Jobs are server-side and durable → **closing the tab never stops a migration**; SignalR reconnects on return.

| Step | Purpose | Notes |
|---|---|---|
| 1. Source → Destination | Pick the two providers | The triangle |
| 2. **Connect** | Per-provider auth | Offers only supported auth methods; **inline hand-held setup guide**; **mandatory "Test connection" gate** — no proceeding on unverified creds (fail in 5s at step 2, not hour 3) |
| 3. Scope | What to migrate | **Single ⇄ Batch toggle**: single = one mailbox; batch = mailbox-pair table + CSV import. Folder/date/filters under **"Advanced"** (hidden) |
| 4. Pre-flight & Plan | Issues + recommended fixes + quota check | Ends in explicit **"Approve plan"** gate |
| 5. Run | Live progress | SignalR: per-mailbox/folder bars, throughput, ETA, streaming error feed |
| 6. Results | Summary | Audit log (subject/date/folder), **"needs decision" queue**, **re-run** (idempotent, free) |

**Persona reconciliation = progressive disclosure.** Old man sees the linear happy-path; MSP expands "Advanced" / switches to Batch. **One wizard, one codebase, no "pro mode" fork.**

---

## 14. Billing (Hosted only)

**Hybrid model: per-mailbox base + fair-use volume cap.** Keeps pricing predictable and quotable (what the personas need) while protecting margin against pathological "whale" mailboxes (the economic concern behind volume billing).

- **Tiered monthly subscriptions with an included mailbox quota** (e.g., $20 → 200 mailboxes/month; higher tiers above). **Cancel-anytime** — so the one-time migrator pays one month and leaves; the MSP keeps paying.
- **A "consumed mailbox" = one unique source→dest mailbox *pair* per billing period.** Resumes/retries/re-runs of the same pair are **free** (keyed off the idempotency ledger). **Reliability never costs the customer extra.**
- **Fair-use volume cap *per mailbox*** (e.g., up to ~50 GB / ~100k messages — exact figure TBD, see §19). Normal mailboxes never notice. A mailbox exceeding the cap triggers an **overage charge or higher tier**. This is where "volume" enters billing without making the *base* price unpredictable.
  - Rationale: GB correlates with cost better than message count (attachments dominate bandwidth); the cap is best expressed in **GB with a message-count companion**.
- **Quota enforced at pre-flight / plan-approval — NEVER mid-migration.** Pre-flight already enumerates the plan's mailboxes *and their volume* → "this job needs 30 mailboxes (you have 12 left), and 2 mailboxes exceed the 50 GB fair-use cap → upgrade to proceed" *before* anything starts. A migration must never die mid-run on a billing wall.
- **Stripe Billing.** Self-hosted has no billing.
- Concierge priced separately as a service.

---

## 15. Repository & Solution Structure

Open-core boundary enforced **physically by repo split**.

- **Public monorepo** = the entire free OSS tool.
- **Private repo** = the hosted layer; **consumes the OSS engine as a published NuGet package** (nothing proprietary can leak into OSS).

### Public monorepo layout

```
/src                              # .NET solution
  EMaigrator.Core                 # canonical model + INTERFACES + error catalog
                                  #   + pre-flight + idempotency/ledger logic
                                  #   PURE LOGIC, NO I/O — references NOTHING
  EMaigrator.Connectors.Imap      # \
  EMaigrator.Connectors.Graph     #  > DI-discovered plugin assemblies
  EMaigrator.Connectors.Gmail     # /  (community-contributable)
  EMaigrator.Infrastructure       # EF/Postgres, MassTransit/RabbitMQ,
                                  #   KMS ISecretStore impls, OTel wiring
  EMaigrator.Workers              # queue-consumer background services
  EMaigrator.Api                  # ASP.NET Core REST + SignalR hub
  EMaigrator.Cli                  # CLI
  *.Tests                         # mirrored test projects per module
/web                              # Vite + React + TS SPA
/deploy                           # docker-compose (app + Postgres + RabbitMQ + Redis) + containerfiles
/docs                             # self-host guide, connector-authoring guide,
                                  #   error-catalog contribution guide
```

### Dependency rule
`Core` depends on **nothing**. Connectors + Infrastructure depend on `Core`'s interfaces. Api/Workers/Cli compose them via DI. **The engine never references infrastructure** — this prevents circular dependencies and keeps the core unit-testable.

### Key DI interfaces (the seams)
- `ISourceProvider` / `IDestinationProvider` — provider plugins
- `ISecretStore` — credential storage (KMS vs local)
- `IJobOrchestrator` — queue/worker orchestration (MassTransit vs future Temporal)

---

## 16. Tech Stack & Versions

| Layer | Choice |
|---|---|
| Backend | **C# / .NET 10 (LTS)**, ASP.NET Core |
| Real-time | SignalR |
| Messaging | MassTransit over RabbitMQ (transport-swappable) |
| Cache / rate-limit / backplane | Redis (distributed token buckets + SignalR backplane) |
| Database | PostgreSQL + EF Core |
| Secrets | KMS envelope encryption (Azure Key Vault / AWS KMS) via `ISecretStore` |
| Observability | OpenTelemetry (Serilog → OTLP) |
| Frontend | Vite + React 19 + TypeScript |
| Runtime (web tooling) | Node 24 LTS |
| Payments (hosted) | Stripe Billing |
| AI fallback (optional) | Cheap model — Kimi / Claude Haiku |

> Versions are sensible defaults (current LTS as of 2026-06); confirm at scaffold time.

---

## 17. Testing Strategy

"Full coverage" is **defined**, not naive — EMaigrator's job is I/O against external mail servers that cannot be deterministically unit-tested. The DI seams ([§15](#15-repository--solution-structure)) exist precisely to make the engine testable.

| Layer | Approach | In coverage %? |
|---|---|---|
| **Deterministic core** (canonical model, hash/idempotency, sanitization, depth-flattening, error catalog, pre-flight planner) | Unit tests — **target ~100%**. *This is "full coverage."* | ✅ |
| **Provider boundary** | Contract tests with in-memory fakes implementing the provider interfaces | ✅ |
| **IMAP path + full E2E pipeline** | **Testcontainers**: Postgres + RabbitMQ + real containerized IMAP (GreenMail/Dovecot); proves idempotency + crash-resume | ✅ |
| **Graph / Gmail connectors** | **WireMock.Net** with fixtures **recorded from real APIs** (not guessed). | ✅ |
| **Live smoke** | Gated, credentialed, pre-release/nightly — NOT per-commit | ❌ (excluded from coverage %) |

**Test tenants:** free **M365 Developer Program** tenant is used to record real fixtures + occasional smoke (no reason to skip the free one). Paid **Google Workspace** live testing is **deferred** — documented risk: *until a real Google migration runs, the Gmail connector is validated only against recorded fixtures.*

---

## 18. Cross-Cutting Principles

- **Self-host / cloud dependency parity** — every dependency must have a container-friendly OSS version a self-hoster can `docker compose up`, *and* a path to scale in hosted. Rules out cloud-locked primitives in the engine (Service Bus, SQS, proprietary auth/monitoring SaaS). Realized via: RabbitMQ (queue), Postgres (DB), Redis (rate-limit/backplane), `ISecretStore` (secrets), OpenTelemetry (observability), ASP.NET Identity (auth).
- **Transparency at signup** — disclose in bold: no bodies stored, metadata 30 days, credentials encrypted + purged on completion.
- **Idempotency is load-bearing** — it enables resume, dedup, free retries (billing), and lets us avoid a heavyweight workflow engine.
- **Pre-flight earns its keep 3×** — error detection, billing-quota check, and the approval gate are one screen.

---

## 19. Open Items / TBD

1. ~~WorkMail end-of-service date~~ — **RESOLVED.** See [§4.1 Timeline & Release Phasing](#41-timeline--release-phasing).
2. **Detailed wizard screens** — step-by-step visual/interaction spec beyond the flow in [§13](#13-frontend--ux).
3. **Pricing tiers** beyond the $20/200 example, **and the exact fair-use volume cap** (GB + message-count per mailbox) + overage rate.
4. ~~Stack versions~~ — **CONFIRMED:** .NET 10 (LTS), React 19, Node 24.
5. **Concierge service** operational model — likely out of scope for *this* software doc.

---

## 20. Decision Log

Compressed rationale for the non-obvious calls (full reasoning lives in the design interview):

| # | Decision | Why |
|---|---|---|
| Open-core + hosted + concierge | Reconciles "open-source first" with "cash in" | Free engine = trust/SEO funnel; hosted removes setup pain; concierge = labor margin |
| Hub-and-spoke canonical model | Kills the N×M connector trap | Adding a provider = one plugin |
| Native-first, IMAP fallback | Reliability over universality-first | Native handles Gmail/MS365 quirks; IMAP covers everyone else |
| BYO OAuth apps in v1 | **Avoids Google CASA + MS publisher verification entirely** | We operate no shared branded app → verification burden sits with the customer's admin |
| Streaming pass-through, no body storage | Privacy + compliance + cheap | Truthful "we never store email"; minimal GDPR/breach surface |
| Canonical-identity hash, never raw bytes | Correct dedup | Servers rewrite messages in transit → raw-byte hashes cause false dupes |
| Durable queue + idempotent workers (not Temporal) | Idempotency already bought durability | Ledger-as-state; resume = re-enqueue |
| PostgreSQL over SQL Server | Open-core friendliness | No licensing friction for self-hosters |
| Rule catalog over AI-first | Testable, offline, finite problem space | AI is an optional fallback for the unknown tail, never auto-fixes |
| Pre-flight + explicit plan-approval | "Inform user, no silent defaults" *and* walk-away UX | Batch all structural decisions up front; transient retries stay automatic |
| Subjects toggle, no PII scrubber | Keepable promise | A leaky 95% scrubber stores PII while claiming it doesn't; a binary toggle is 100% honest |
| Credentials purged on job-terminal (≠ 30-day logs) | Minimize standing-access risk | Secrets are live keys, not metadata |
| Vite SPA over Next.js | C# backend already exists | Next SSR = a second server runtime for zero gain |
| Subscription + quota, cancel-anytime | Serves both personas | MSP = recurring; old man = one month then cancel |
| Hybrid billing: per-mailbox + fair-use volume cap | Predictable/quotable price *and* margin protection | Pure volume = "can't quote until we scan" + surprise bills for packrat mailboxes |
| Consumed-mailbox = unique pair/period, retries free | Billing must not punish reliability | Keyed off idempotency ledger |
| Quota check at pre-flight, never mid-run | A migration must never die at mailbox 201 | Pre-flight already enumerates mailboxes |
| Public monorepo + private hosted repo (NuGet) | Open-core boundary as a physical line | Proprietary code cannot leak into OSS |
