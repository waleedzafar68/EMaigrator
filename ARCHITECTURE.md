# EMaigrator — Execution & Parallelism Architecture

> **Status:** Locked. Deep-dive on *how migrations execute concurrently* — elaborates `DESIGN.md §8`, does not repeat it.
> **Audience:** Backend engineers / agents implementing the workers, orchestration, and rate-limiting.
> **Read `DESIGN.md` first** for the engine, idempotency ledger, and queue/stack choices.

---

## 1. Execution Hierarchy

A migration decomposes top-down; parallelism is applied at each level with a cap:

```
Migration            = one source→dest MAILBOX PAIR        (the billing unit)
  └─ FolderTask      = one source folder → dest folder
       └─ Batch      = N messages (small, e.g. 50–200)
            └─ Message = one copy operation (the idempotent atom)
```

- The **Message** is the atomic, idempotent unit (`DESIGN.md §6`): keyed by canonical identity, checkpointed in the ledger.
- The **Batch** is the unit of work pulled from the queue and acknowledged.
- The **FolderTask** is the unit of fan-out within a mailbox.
- The **Migration** is the unit of scheduling, billing, and (for the operator) progress.

---

## 2. Where Parallelism Happens (three axes + caps)

| Axis | Parallelism | Bound by |
|---|---|---|
| **Across migrations / mailboxes** | High | Global worker-pool size; per-tenant concurrency cap |
| **Across folders within a mailbox** | Moderate | Per-mailbox folder-concurrency cap |
| **Messages within a folder** | Batched | Batch size; the **per-account rate-limit token bucket** ([§5](#5-rate-limit-coordination-the-hard-part)) |

The real throttle is **never raw thread count** — it's the **provider rate limit** ([§5](#5-rate-limit-coordination-the-hard-part)). Worker concurrency is sized generously; the token buckets are what actually pace the work.

**No cross-message ordering requirement.** Each email is independent and its original timestamp is preserved (`InternalDate` / Graph `receivedDateTime`), so messages within and across folders may be migrated in any order. This is what *permits* free parallelism — the only ceiling is the provider's rate limit.

---

## 3. Worker Pool & Horizontal Scaling

- Workers are **stateless MassTransit consumers** (`DESIGN.md §8`) running as background services.
- **Scale horizontally** by adding worker instances — the queue distributes batches across all consumers (competing-consumers).
- Each consumer has a **concurrent-message limit** (prefetch) capping how many batches it processes at once.
- **State lives only in Postgres** (the ledger) and the queue — so any worker can pick up any work, and a dead worker's in-flight batches simply become visible again (un-acked) and are re-delivered.

```
        ┌── worker #1 ──┐
queue ──┼── worker #2 ──┼──►  (each: prefetch=K concurrent batches)
        └── worker #N ──┘     add instances → more throughput,
                              bounded by token buckets (§5)
```

---

## 4. Rate-Limit Coordination (the hard part)

**The problem:** provider limits are enforced **per account/tenant**, but many workers may hold batches for the *same* account simultaneously. Uncoordinated, they collectively blow the limit and trigger mass 429s — which is exactly how naive migrators stall.

**The solution — a shared distributed token bucket per account in Redis:**

- A **token bucket per `(provider, account)`** lives in **Redis**, updated by an **atomic Lua script** (token-bucket / sliding-window). Refill rate = the provider's documented sustained limit; burst capacity for headroom.
- **Before each provider call, the worker acquires a token** from that account's bucket. No token → the worker backs off and the batch is retried later. Workers stay **fully stateless and uniform** — *any* worker can process *any* account's batches, so load distributes evenly across the pool.
- Independent buckets per account → **full parallelism across accounts**, strict pacing **within** an account.
- If a worker dies mid-batch, un-acked batches redeliver to other workers; the **ledger guarantees no double-copy** (`DESIGN.md §6`). No partition pinning to rebalance.
- **Redis is open-source + container-friendly** (a 4th lightweight container), so this honors the parity principle. It also **doubles as the SignalR backplane** (`DESIGN.md §8`) — progress events from any worker reach a client connected to any API instance.

> **Alternative (not chosen):** pin each account's work to a single consumer via a **consistent-hash exchange** keyed by `(provider, account)`, enforcing an *in-process* bucket — this needs no Redis, but caps a high-volume account to one worker's throughput and complicates rebalancing. With Redis available (open-source, already pulling backplane duty), the shared-bucket approach wins on utilization and routing simplicity. Keep partitioning as the fallback if Redis is ever dropped.

---

## 5. Backpressure & Adaptive Backoff

- On **429 / throttle / `Retry-After`**: the account's **Redis token bucket** is **drained/paused for the indicated duration** — *only that account's* work pauses; every other account keeps flowing.
- Backoff is **adaptive**: repeated throttling lowers the bucket's effective refill rate (multiplicative decrease); sustained success lets it recover toward the cap (additive increase). This auto-tunes to whatever the provider is actually allowing right now.
- This is the signal behind the UI's **"Slowing to respect provider limits"** chip and the **throttling buffer** baked into the ETA.

---

## 6. Idempotency, Checkpointing & Resume Under Parallelism

- Each message copy is **checkpointed to the ledger** as it completes (or per small batch).
- **Resume = scan ledger for not-done items in the migration, re-enqueue them.** Works identically whether the interruption was a crash, a deploy, a Pause, or a rate-limit abort.
- Because checkpoints are per-message and operations are idempotent, **at-least-once delivery is safe** — a redelivered batch re-checks the ledger and skips already-copied messages. No exactly-once machinery needed.
- **Pause/Resume** = stop pulling new batches for the migration + let in-flight batches drain → resume re-enqueues remaining ledger items.

---

## 7. Failure Isolation

Failures are contained at the smallest possible scope so one bad thing never stops a good thing:

| Failure | Containment |
|---|---|
| **One poison message** | Parked in the **dead-letter queue** after retries; surfaces in the post-run "needs decision" queue. Its batch/folder/mailbox continues. |
| **One folder errors** | That FolderTask fails; siblings continue; failure recorded in the ledger. |
| **One mailbox fails (in a batch)** | That migration is marked failed/partial; the **other 217 mailboxes are unaffected**. |
| **One worker dies** | Its un-acked batches redeliver to other workers; partitions rebalance; ledger prevents double-work. |
| **One account is throttled** | Only that account's bucket pauses; other accounts flow. |

---

## 8. Configuration Knobs

All tunable (config / per-deployment); sensible defaults shipped:

- **Global max concurrent migrations** (worker-pool sizing).
- **Per-tenant concurrency cap** (fairness in multi-tenant hosted).
- **Per-mailbox folder-concurrency cap.**
- **Batch size** (messages per queue item).
- **Per-(provider, account) token-bucket** refill rate + burst (defaults per the provider's published limits; adaptive at runtime).
- **Consumer prefetch** (concurrent batches per worker).
- **DLQ retry count / backoff schedule** before parking a poison message.

---

## 9. Worked Example

*MSP migrates 218 WorkMail mailboxes → Microsoft 365.*

1. 218 **Migrations** enqueued; worker pool picks up as many as the global cap allows (say 16 concurrently).
2. Each running migration fans out its folders into **FolderTasks**, batched into ~100-message **Batches**.
3. Batches route by `(Graph, <dest-tenant-account>)` → since all 218 land in **one** destination M365 tenant, the **destination** rate limit is the binding constraint; the consistent-hash partition for that tenant **paces all writes through one token bucket** → no 429 storm.
4. Source reads hit **218 different WorkMail accounts** → those partition widely → high read parallelism.
5. Graph throttles → that tenant's bucket backs off adaptively; UI shows `throttled`; ETA already buffered for it.
6. A worker crashes mid-batch → un-acked batches redeliver; ledger skips the messages already written; **zero duplicates**.
7. 3 messages exceed size cap → DLQ → post-run "needs decision"; everything else completes.
8. Operator resolves the 3, clicks **Re-run unfinished** → only those 3 are re-enqueued (idempotent, free).

---

## 10. Summary Diagram

```
 Dashboard/API ──enqueue──► [ Migration queue ]   ◄── Redis: SignalR backplane
                                   │                    + per-account token buckets
                                   ▼                              ▲
        ┌──────────────── stateless consumers (any→any) ──────────┼──┐
        │   acquire token (provider,account) ────────────────────┘  │
        │     │ folders ‖           │ folders ‖                      │
        │     ▼ batches            ▼ batches                         │
        │  read src → canonical → write dst   (streaming, §DESIGN)   │
        └───────────────┬─────────────────────────┬──────────────────┘
                        │ checkpoint per msg       │ poison → DLQ
                        ▼                          ▼
                 [ Idempotency ledger ]     [ needs-decision queue ]
                  (Postgres = state)
                        │
              resume = re-enqueue not-done items
```
