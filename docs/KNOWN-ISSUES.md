# Known issues

Tracked, non-blocking defects with a known data-safety bound and a fix direction. Update or remove an
entry when it is fixed.

## RESOLVED 2026-06-09 — Graph MIME import + custom-folder resolution (`EMaigrator.Connectors.Graph`)

Two live-only defects (both shipped green because WireMock validates neither the body nor the live
`parentFolderId` shape) were root-caused against a live tenant and fixed; verified end-to-end by the gated
`GraphDestinationLiveTests`. Kept here for the non-obvious root causes:

1. **`UnableToDeserializePostBody` on every MIME write.** The folder-scoped endpoint
   `POST .../mailFolders/{id}/messages` **silently rejects** a `text/plain` base64-MIME body (it only
   deserializes a JSON `message` resource there — Graph's *JSON* error, distinct from the MIME path's
   `ErrorMimeContentInvalidBase64String`), despite the docs listing it as a MIME target. A raw `HttpClient`
   POST bypassing the SDK failed identically, exonerating Kiota and the MIME bytes. **Fix:** import MIME at
   the **top-level** `POST .../messages` (which Graph accepts → creates a draft in Drafts), then `move` it
   into the destination folder. Graph marks every MIME-imported message `isDraft=true` with no supported way
   to clear it after creation (confirmed live + by expert consensus), so we at least preserve source
   read/unread via the mutable `isRead` (a draft-flag mitigation via FTS export/reimport is a future option).

2. **Custom top-level folders never resolved.** Live Graph returns a top-level folder's `parentFolderId` as
   the mailbox root's **real id** (the literal `"msgfolderroot"` is only a URL alias) and never returns the
   root itself in the folder list, so `GraphFolderMapper` treated every custom folder as an orphan and
   dropped it. Well-known folders survived only via name aliases. **Fix:** `GraphMailFolderNode.BuildFromGraph`
   treats a parent that is absent from the complete fetched set as the root.

## Live Gmail→Exchange validation findings (2026-06-09) — tracked follow-ups

A full live reconcile (`alice.chong@bellfield`, Google Workspace → M365, ~650 labels, **320 messages copied,
0 write failures**) confirmed the write fix end-to-end and surfaced these. None block the write path:

- **Imported messages are drafts — ACCEPTED for v1.** Every MIME-imported message is `isDraft=true`
  (confirmed 200/200 in one folder); Graph offers no supported way to clear the flag after creation.
  Read/unread IS preserved (`isRead`). Decision: ship as-is. The FTS export→edit-flags→reimport workaround
  (Glen Scales) is the documented path if true non-draft fidelity is later required.
- **Gmail labels map to separate Exchange folders.** A message with multiple labels (e.g. `INBOX` +
  `IMPORTANT` + `CATEGORY_*`) is copied into EACH corresponding folder — the reconcile diffs per live-dest
  folder — so the same message lands in several folders. Expected given the label→folder mapping; revisit
  if single-folder placement is desired.
- **Reconcile is ~O(folders²) on the folder list.** Each folder scanned re-fetches the whole mailbox folder
  tree (`FetchFolderNodesAsync`), so a 650-folder mailbox issues ~650 full folder-list fetches. Correct but
  slow; cache the tree per reconcile run.
- **RESOLVED 2026-06-09 — Reconcile progress + job status now surface in the UI.** `ReconcileConsumer` now
  publishes a per-folder `MigrationProgressEvent` carrying a nested `ReconcileProgress`
  (foldersDone/folderTotal/copied/backfilled/skipped) so the reconcile Run view shows live folder-based
  progress, and a mode-agnostic `EfJobStatusFinalizer` rolls the parent `jobs.Status` to terminal once all
  its mailboxes are terminal (idempotent; gates on all-mailboxes-terminal so the resume-completion race
  below does not regress). Both migrate and reconcile publish a terminal `MigrationProgressEvent` so the
  SignalR `StatusChanged` fires once. The compose `api` service sets `Cors__AllowedOrigins__0`
  (`${WEB_ORIGIN:-http://localhost:3000}`) so a cross-origin SPA can negotiate the hub. (The ~O(folders²)
  folder-list re-fetch and EF Information-level command logging below remain open.)
- **EF command logging runs at Information in Production**, flooding worker logs with every SQL statement
  during a run; lower the `Microsoft.EntityFrameworkCore.Database.Command` category level.

## Resume-completion race (`EMaigrator.Workers`)

**Severity:** low — misleading status only. **No data loss, no duplication.**

**Symptom.** A single `resume` of an already-finished migration can report `Completed` *before* the
re-seeded items finish.

**Mechanism.** `resume` reopens the migration to `Running` and re-publishes `StartMigration`, which
re-seeds `Pending` ledger rows. But `MigrationCompletionConsumer` writes the terminal status the instant
it observes `Pending == 0` (`counts.Pending == 0` → `SetTerminalAsync`). During a resume there is a
transient window where the completion consumer can observe `Pending == 0` (the re-seed has not landed
yet) and write a **premature terminal status**, driven by a lingering/redelivered
`MigrationProgressEvent`.

**Why it's safe.** Resume is idempotent: copies are gated by `ILedger.IsDoneAsync` (no re-copy) and
`SeedPendingAsync` never downgrades a done/failed row. `SetTerminalAsync` is itself idempotent. A
subsequent `resume` finishes any remaining work. So the only consequence is a temporarily misleading
status — never a lost or duplicated message.

**Do NOT "fix" by removing the seed-Pending-up-front design** — that design (seed all `Pending` before
publishing any folder, so `Pending` only ever decreases and `Pending == 0` unambiguously means complete)
is what eliminates the *original*, worse distributed-completion race. The proper fix belongs in the
engine: gate the completion consumer so it does not conclude terminal for a *reopened* migration until
its re-seed / fan-out-complete marker has run, or drain stale progress events on re-enqueue.

**Code sites:** `Consumers/MigrationCompletionConsumer.cs`, `Consumers/StartMigrationConsumer.cs`,
`Consumers/JobControlConsumer.cs`, `Persistence/EfMigrationStatusWriter.cs` (`SetTerminalAsync`).

## Retry policy has no delayed backoff (`EMaigrator.Workers`)

**Severity:** low — operational. Both transports use `UseMessageRetry(r => r.Immediate(DlqRetryCount))`
(`WorkerServiceRegistration.cs`), so on a provider `429` the batch retries **immediately** with no
`Retry-After` honored — the rate-limiter bucket penalty is the only pacing. The `429`'s `Retry-After` is
dropped because `WriteResult` (a frozen Core contract) has no field for it. Adding delayed/interval retry
(or the RabbitMQ delayed-message-exchange plugin) is a deferred follow-up that also needs a CONTRACTS
change to carry `Retry-After`. A maintainer tuning `DlqRetryCount` or diagnosing 429 storms needs this
context.

## Frontend live API/SignalR integration is not wired (Wave F)

**Severity:** expected — tracked work, not a defect. The SPA (`/web`) calls relative `/api/v1` +
`/hubs/migrations` with **no Vite dev proxy** and CSP `connect-src 'self'`; only `VITE_USE_MOCKS=1` makes
it run standalone. Going live (Wave F) requires deciding same-origin (reverse-proxy `/api` + `/hubs` to
the .NET API) vs cross-origin (then widen the `index.html` CSP `connect-src`, confirm cookie `SameSite`,
and revisit the hub's no-`accessTokenFactory` stance), plus the tracked items: usage-data wiring,
API-authoritative `canBatch`, cross-origin WS, per-row dashboard live updates.
