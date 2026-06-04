# Known issues

Tracked, non-blocking defects with a known data-safety bound and a fix direction. Update or remove an
entry when it is fixed.

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
