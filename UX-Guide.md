# EMaigrator — UX Guide

> **Status:** UX design-locked, ready for implementation.
> **Audience:** Implementing agents and frontend engineers. Pairs with `DESIGN.md` (architecture). This doc owns flows, screens, states, copy, and interaction rules.
> **Scope:** The **operator-facing application** (Vite + React SPA). Marketing/docs site is out of scope.
> **Last updated:** 2026-06-01

---

## 1. UX Principles

These five principles resolve every ambiguity below. When in doubt, apply them in order:

1. **Progressive disclosure.** One UI serves both a non-technical individual and a power MSP. The happy path is clean and linear; complexity (advanced options, dense views, technical error detail) is *collapsed by default, one click away*. **There is no "pro mode" fork** — same screens, expandable depth.
2. **Reassurance at anxiety points.** People are moving something precious (their email). Surface safety facts exactly where fear lives: *"Your source is never changed,"* *"We never store your email,"* *"Safe to close — runs in the background."*
3. **Fail fast, fail clearly.** Catch problems at the earliest possible step (test-connection gate, Review & plan) — never mid-migration. Every error says **what happened + what to do**, never a raw stack trace (technical detail is expandable, see [§8.2](#82-error-message-pattern)).
4. **Plain language, no jargon.** "Migration," "From/To," "Mailbox," "Review & plan." Never "job," "pre-flight," "source/destination" in user-facing copy.
5. **Truly portable.** Mobile-responsive throughout; the single-mailbox path is mobile-first and excellent.

---

## 2. Personas (recap)

See `DESIGN.md §2`. The UX tension is the **skill spread in one UI**:

- **MSP / IT consultant** — hundreds of mailboxes, recurring; needs batch, CSV, overrides, technical error detail.
- **Self-serve individual ("the old man")** — one mailbox, one-time; needs a dead-simple linear path and reassurance.

The mailbox owners being migrated are **not** users.

---

## 3. Terminology & Voice

### Vocabulary (user-facing)

| Use | Not | Notes |
|---|---|---|
| **Migration** | "job," "transfer," "task" | Also the SEO term |
| **From / To** | "source / destination" | "Source/destination" allowed in docs/technical only |
| **Mailbox** | "account" | Onboarding/welcome copy may say "your email" |
| **Connect** | "authenticate," "authorize" | |
| **Review & plan** | "pre-flight," "scan" | "Pre-flight" is internal/DESIGN-only |
| **Migration** (dashboard item) | "job" | |

### Voice

Calm, warm, plain, competent — **never condescending**. Short, active sentences. Reassurance surfaced at anxiety points. Errors are help, not blame.

- ✅ *"We couldn't sign in to WorkMail. WorkMail needs an app password, not your normal password — here's how to create one."*
- ❌ *"AUTH_FAILED: IMAP NO [AUTHENTICATIONFAILED] Invalid credentials (Failure)."* (← this lives behind "Technical details")

---

## 4. Navigation & The Wizard Shell

### Dashboard is home
All migrations (drafts, running, completed, failed) live on the **Dashboard** ([§5](#5-the-dashboard)). The wizard is entered *from* it and returns *to* it.

### The wizard is a linear, gated stepper
```
  ●━━━━━●━━━━━○─────○─────○─────○
  From   Connect  Scope  Review  Run  Results
  & To            & plan
```
- **Forward-gated:** cannot advance past an incomplete/unverified step (e.g., no passing Connect without a green test-connection).
- **Back always allowed.**
- **Smart defaults** so the happy path is `Next → Next → Test → Approve → Run`.

### Setup itself is resumable (server-persisted Draft)
- A **Draft migration** is created server-side at Step 1 and **autosaved per step**, tied to the operator's account.
- **Consequence:** close the tab mid-setup (e.g., to go create an Azure app, or because the laptop closed) → the **Draft appears on the dashboard**; reopen it and resume exactly where you left off.
- Credentials entered into a Draft follow the same encryption + purge rules as a live migration (`DESIGN.md §10`).
- An account may hold **multiple Drafts**.
- A **"Reset / Start over"** action discards a Draft and begins clean.

---

## 5. The Dashboard

```
┌─ Migrations ───────────────────────────────  [ + New Migration ] ─┐
│  Usage: ▓▓▓▓▓▓░░░░ 128 / 200 mailboxes this month   · Upgrade      │
│  [ All ▾ ] [ Search ]                          [ ⊞ cards / ☰ list ] │
│                                                                     │
│  ● WorkMail → Microsoft 365     Running · 58% · 126/218   [View]    │
│  ◐ WorkMail → Google            Draft · step 2 of 6       [Resume]  │
│  ✓ Zoho → Microsoft 365         Completed · 2d ago        [Results] │
│  ⚠ WorkMail → Microsoft 365     Partial · 3 need decision [Results] │
└─────────────────────────────────────────────────────────────────────┘
```

**Elements:**
- **"+ New Migration"** — always-present primary CTA → enters the wizard (creates a Draft).
- **Migration rows/cards** show: From → To (provider icons + plain text), scope (1 mailbox / N mailboxes), **status + live progress** (running migrations update via SignalR in place), last activity, and a **context action** (Resume / View / Results).
- **Filter by status + Search;** active migrations pinned on top.
- **Card ⇄ list density toggle** — **cards default** (friendly), **list/table** for managing many migrations.
- **Usage widget (hosted only):** persistent mailbox consumption + Upgrade, so the MSP sees usage *before* Review & plan blocks them.

**Empty / first-run state (dedicated welcome):** not an empty table. One line of what EMaigrator does + a single big **"Start your first migration."** This is the non-tech first impression — make it warm.

---

## 6. The Migration Wizard

### 6.1 Step 1 — From & To
Trivial by design. Two provider pickers (the v1 triangle: WorkMail / MS365 / Google) + a **plain-language summary**: *"You're moving mail **from** WorkMail **to** Microsoft 365."* → Next.

### 6.2 Step 2 — Connect  *(split: 2a Connect From, 2b Connect To)*

The highest-risk screen. Split into **two test-gated sub-steps** (each endpoint), shown as distinct stepper stops. Each adapts to the **auth methods that provider supports** (`DESIGN.md §11`), with the persona-appropriate default highlighted.

```
┌─ Connect From: Amazon WorkMail ──────────────────────────┐
│  How do you want to connect?                              │
│   (•) Username & password (IMAP)        ← recommended      │
│   ( ) Advanced / custom server                            │
│                                                            │
│  Provider preset:  [ Amazon WorkMail ▾ ]                  │
│  Region: [ us-east-1 ▾ ]  → How do I find my region?      │
│  Server: imap.mail.us-east-1.awsapps.com   Port: 993 🔒   │
│  Username: [______________]  Password: [____________]     │
│                                                            │
│  ⓘ WorkMail needs an app password, not your console       │
│     password.  → How to create one                        │
│                                                            │
│  🔒 We read mail to migrate it. We never store contents.  │
│            [ Test connection ]   → must pass to continue   │
└────────────────────────────────────────────────────────────┘
```

**Three auth paths:**

1. **IMAP (individual / WorkMail / v2 providers):**
   - **Parameterized provider presets** — host is a *template*, not a static string. **WorkMail** = `imap.mail.{region}.awsapps.com` + a **region dropdown** (only 3 regions: us-east-1, us-west-2, eu-west-1) + **"How do I find my region?"** helper. Static-host providers (Zoho, Hostinger…) need no parameter.
   - **"Advanced / custom server"** is the always-available escape hatch (manual host/port/SSL).

2. **BYO OAuth (MSP — the gnarly path):**
   - An **inline, numbered, guided checklist** *on the screen* (never a docs link that navigates away): copy-paste values (redirect URI, required permissions e.g. `Mail.ReadWrite`, admin-consent), **deep-links into the Azure/Google portal**, and paste-back fields (Tenant ID, Client ID, Secret / service-account JSON).
   - **Screenshots per step** — annotated, *supplementary*. **Text instructions must stand alone** so a stale screenshot never blocks. Guides are stored as **updatable content assets** (markdown + images, served from storage), **not hardcoded** — with an owned **refresh process** (Azure/Google portals redesign often).
   - A **"I already have an app — just let me paste credentials"** toggle lets experienced MSPs skip the hand-holding.

3. **Mandatory Test Connection gate (all paths):**
   - Proves **read access** (From) / **write access** (To) — not just auth.
   - Success is concrete: *"Connected — found 14 folders, 3,201 messages."*
   - Failure returns a **specific, catalog-driven, actionable error** (see [§8.2](#82-error-message-pattern)), e.g. *"Authentication failed — WorkMail requires an app-specific password. → Create one."*
   - **Green is required to advance.**

### 6.3 Step 3 — Scope

**Adapts to the Step 2 connection type** — this dependency is mandatory:

- **Connected with single-mailbox creds** (IMAP / delegated OAuth): scope is **Single, pre-determined** — show *"Migrating: `oldman@biz.com` → `oldman@gmail.com`,"* just confirm. The **Batch** toggle is **disabled with an explanation**: *"To migrate multiple mailboxes, reconnect using admin access."*
- **Connected with admin/app creds** (domain-wide delegation / application permissions): **Single ⇄ Batch** toggle is live.
  - **Single:** pick one mailbox from a searchable list of the tenant's mailboxes.
  - **Batch:** a **source→destination mapping table**. **CSV import is primary** (columns `source_mailbox, destination_mailbox`), with **in-app pair-building as fallback** (pick From, pick To). Inline validation flags non-existent destination mailboxes; per-row status.
- **Under "Advanced" (collapsed):** folder selection (all by default; include/exclude), date-range filter, folder-mapping overrides. The individual never opens this; the MSP does.

### 6.4 Step 4 — Review & plan

The differentiator made visible + the approval gate + the usage check. Opens in a **scanning state** (the read-only analysis can be slow on large mailboxes; **resumable** — keeps scanning server-side if the operator leaves).

**Adaptive layout — clean when clean, detailed when not:**

```
  NO ISSUES (individual):              ISSUES FOUND (MSP / messy mailbox):
  ┌────────────────────────────┐      ┌────────────────────────────────────────┐
  │ ✓ Ready to migrate          │      │ ⚠ 3 things to resolve before we start    │
  │ 1 mailbox · 14 folders      │      │ ▸ 12 folders exceed Outlook's depth      │
  │ 3,201 messages · ~250 MB    │      │     Resolution: Flatten ▾   [details]    │
  │ Estimated: ~12 min          │      │ ▸ 3 folders have illegal characters (/)  │
  │  [ Start migration ]        │      │     Resolution: Sanitize ▾  [details]    │
  └────────────────────────────┘      │ ▸ 8 messages exceed 150 MB size cap      │
                                       │     Resolution: Skip & log ▾ [details]   │
                                       │ Summary: 218 mailboxes · ~1.2M msgs      │
                                       │  [ Approve plan & start ]                │
                                       └────────────────────────────────────────┘
```

- **Issues grouped by type**, each with a **bulk resolution dropdown** (e.g. depth → **Flatten** / Rename / Skip). **Per-folder override hidden under "[details]."**
- **Nothing applies until Approve** — opt-in gate, not silent (`DESIGN.md §7`). Blockers visually distinct from auto-handled items.
- **Time estimate:** show an **average** ("~12 min"), with the **throttling buffer baked in** so it trends conservative (finish-early > finish-late). Never an optimistic promise.
- **Usage check (hosted):** if the plan exceeds the mailbox quota *or* a mailbox exceeds the **fair-use volume cap** (`DESIGN.md §14`), an inline *"Needs 30 mailboxes (you have 12) · 2 mailboxes exceed the 50 GB cap → upgrade to proceed"* **blocks Start** — here, never mid-run.

### 6.5 Step 5 — Run

Adapts single vs batch. Must never *look* frozen, and must reassure that leaving is safe.

```
  SINGLE (individual):                  BATCH (MSP — 218 mailboxes):
  ┌─────────────────────────────┐       ┌──────────────────────────────────────┐
  │ Migrating oldman@biz.com     │       │ ▓▓▓▓▓▓▓▓░░░░  58% · 126/218 mailboxes │
  │ ▓▓▓▓▓▓▓▓▓▓░░░  2,310 / 3,201 │       │ 1,402 msg/min · ETA ~2h 10m            │
  │ Current: /Archive/2023       │       │ [Filter: Failures ▾] [Search]  [density]│
  │ 412 msg/min · ETA ~4 min     │       │ ✓ ceo@biz.com      done  4,001 msgs   │
  │ ⏸ Pause   ✕ Cancel           │       │ ⟳ sales@biz.com    78%   throttled    │
  │ 🔒 Safe to close — runs in    │       │ ⚠ hr@biz.com       needs decision     │
  │    the background.           │       │ … queued: 92                          │
  │ ▸ Activity log               │       │ ⏸ Pause all   ✕ Cancel all            │
  └─────────────────────────────┘       └──────────────────────────────────────┘
```

- **Message-level progress bar** + **current-folder label** + **throughput (msg/min)** + **buffered ETA**.
- **Throttling transparency (required):** when rate-limited, show **"Slowing to respect provider limits" / a `throttled` chip** — never a silently stalled bar. (Prevents users cancelling healthy migrations.)
- **Controls: Pause / Resume / Cancel** (workers + ledger support graceful drain & resume). Batch adds "Pause all / Cancel all" + per-mailbox actions.
- **Batch view:** aggregate bar on top + **filterable (Failures-first) + searchable per-mailbox list** with status chips (done / running / throttled / failed / needs-decision / queued). **Density toggle: light/simple default (individual) ⇄ dense/detailed (MSP insights).**
- **Live activity feed** (collapsible): transient retries shown subtly (auto-handled); structural surprises increment a **"needs decision" counter** for Results.
- **"Safe to close — runs in the background"** always visible.

### 6.6 Step 6 — Results

Serves three needs: confidence, remediation, and (for MSP) a **deliverable**.

```
┌─ Migration complete — Partial ───────────────────────────────┐
│ ✓ 3,180 migrated   ⚠ 3 need your decision   ⤫ 18 skipped      │
│ 1 mailbox · 14 folders · completed in 11m 42s                 │
│                                                                │
│ ⚠ Needs your decision (3)                                      │
│   ▸ /Projects/… folder name collision   [ Resolve ▾ ]         │
│   ▸ message "Q3 budget.xlsx" — 210 MB    [ Resolve ▾ ]         │
│   [ Re-run unfinished items ]   ← idempotent · free            │
│                                                                │
│ Audit log  [search] [filter: failures ▾] [Export report ▾]    │
│   Subject            Date        Folder      Status            │
│   Re: invoice 4521   2024-03-12  /Archive    ✓ migrated        │
│   Quarterly numbers  2024-01-08  /Sent       ⤫ skipped         │
│                                                                │
│ 🔒 This log auto-deletes in 30 days.   [ Delete now ]          │
└────────────────────────────────────────────────────────────────┘
```

- **Four outcome states**, distinct headers: **Success · Partial · Failed · Cancelled.**
- **Completeness summary** up top (migrated / skipped / needs-decision) + **source↔destination count reconciliation** (*"3,201 in source, 3,201 in destination ✓"*) for confidence.
- **Needs-decision queue:** mid-run structural surprises (`DESIGN.md §7`). Model = **resolve the flagged items → one "Re-run unfinished items"** that is idempotent and **free** (same pair) and touches only not-done items.
- **Audit log:** subject / date / folder / status / error (respects the subjects-off privacy toggle), searchable + failures filter.
- **Export report:** downloadable **CSV + PDF** (counts, duration, per-folder breakdown, skipped/failed) — the MSP's **proof-of-work** deliverable / concierge artifact.
- **Purge transparency:** *"auto-deletes in 30 days"* + **"Delete now."**

---

## 7. Notifications

Migrations run unattended for hours — the completion loop must close without the operator watching.

- **Email notifications on terminal states:** Completed · Partial (needs decision) · Failed. **Required for v1** — this is what makes "safe to close / walk away" real.
- **Webhook** (MSP integration) — optional, **v2**.

---

## 8. Cross-Cutting Patterns

### 8.1 Global states
Every screen handles four situations as **reusable patterns**:
- **Loading** — skeletons / "working…", never a blank page (scanning, connecting, list loads).
- **Empty** — friendly, single-focus (dashboard welcome, no-folders, empty log) — never a bare empty table.
- **Error** — clear message + retry (see [§8.2](#82-error-message-pattern)).
- **Reconnecting** — a small **"reconnecting…"** indicator when the SignalR connection blips. Long runs *will* drop connections; this must never *look* like failure.

### 8.2 Error message pattern
**Progressive disclosure applied to errors** (matches Principle 1):
- **Default:** plain-language **what happened + what to do** (catalog-driven where possible — `DESIGN.md §7`).
- **Expandable "Technical details"** (for admins): raw provider error code, API response, and a **support / trace ID** that correlates to OpenTelemetry traces (`DESIGN.md §12`) for support tickets.

```
⚠ We couldn't sign in to WorkMail.
  WorkMail needs an app password, not your normal password.
  → How to create one
  ▸ Technical details        ← collapsed; admin expands
      IMAP NO [AUTHENTICATIONFAILED] …  · trace: 4f9c-21a8
```

### 8.3 Mobile / responsive
**Mobile-responsive throughout — truly portable.**
- **Single-mailbox happy path** (connect via IMAP → review → approve → monitor → results): **mobile-first, excellent.**
- **MSP-only heavy flows** — usable on mobile but **better on desktop by nature:** creating a BYO OAuth app (operator is in the desktop Azure/Google portal anyway) and large batch CSV/table management. Don't pretend Azure setup is great on a phone; do keep it functional.

### 8.4 Accessibility
**WCAG AA baseline:** full keyboard navigation, strong color contrast, screen-reader labels. Plus **generous type sizing & contrast** — the literal "old man" may have aging eyes. Status is never conveyed by color alone (chips carry icons + text).

---

## 9. Decision Log

| Decision | Why |
|---|---|
| One adaptive wizard, no "pro mode" fork | Progressive disclosure serves both personas from one codebase |
| Server-persisted resumable Draft (autosave per step) | Survives the BYO-OAuth detour and the interrupted individual — the single most important shell decision |
| Forward-gated stepper, back allowed | Guided rail keeps non-tech users from getting lost |
| Connect split into From / To, each test-gated | Each endpoint can be a heavy guided flow; combining is overwhelming |
| Parameterized presets (WorkMail = template + region) | A static host string is wrong outside us-east-1 |
| Guided OAuth inline + screenshots as *support* + updatable assets | Portals redesign constantly; text must stand alone, screenshots must be refreshable without redeploy |
| Mandatory test-connection gate | Fail in 5s at Connect, not hour 3 — cheapest insurance against the original tool's misery |
| Scope adapts to connection type | Batch is impossible without admin/app auth — a Batch tab that can't work is a lie |
| CSV-primary + in-app builder fallback | MSP bulk reality + a manual path |
| Review & plan: adaptive layout | A clean "Ready" card for the individual; full issues panel only when needed — don't scare the old man |
| Bulk-by-type resolution, per-item under details | Individual approves; MSP overrides — progressive disclosure |
| ETA = average with throttling buffer baked in | Conservative estimate; finish-early beats a broken promise |
| Throttling shown explicitly | A silent slow bar gets healthy migrations cancelled |
| Pause/Resume + density toggle on run view | Big-job control + light-default / dense-on-demand monitoring |
| Resolve-then-re-run (idempotent, free) | Reliability never costs the customer |
| CSV + PDF proof-of-work report | MSP client deliverable / concierge artifact |
| Source↔destination reconciliation | Real completeness confidence |
| Non-jargon vocabulary | Jargon quietly alienates the non-tech persona |
| Email notifications on terminal states | Closes the "walk away" loop — required, not optional |
| Full mobile, single-path mobile-first | "Truly portable"; heavy MSP flows are desktop-practical by nature |
| Errors: plain default + expandable technical details | Old man gets help; admin gets the trace ID for support |
