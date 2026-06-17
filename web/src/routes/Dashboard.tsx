import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ArrowRight, LayoutGrid, List } from "lucide-react";
import { listMigrations } from "../api/migrations";
import { MigrationsHubClient } from "../api/signalr";
import type { JobStatus, MigrationDto } from "../api/types";
import { type ChipStatus, jobStatusToChip, StatusChip } from "../components/StatusChip";
import { ProviderRoute } from "../components/ProviderRoute";
import { ProgressBar } from "../components/ProgressBar";
import { buttonVariants } from "../components/ui/button";

type Layout = "cards" | "list";

// Non-terminal states whose rows we keep live via the hub.
const NON_TERMINAL: ReadonlySet<JobStatus> = new Set<JobStatus>([
  "Queued", "PreFlight", "AwaitingApproval", "Running", "Paused",
]);

function isNonTerminal(status: JobStatus): boolean {
  return NON_TERMINAL.has(status);
}

// Status → left-accent bar color, so a row's state reads at a glance (paired with the icon+label chip).
const ACCENT_BAR: Record<ChipStatus, string> = {
  done: "bg-success", running: "bg-accent", throttled: "bg-throttled",
  warning: "bg-warning", error: "bg-error", queued: "bg-idle",
};

function actionFor(m: MigrationDto): { to: string; label: string } {
  if (m.status === "Draft") return { to: `/migrations/${m.id}`, label: "Resume" };
  if (
    m.status === "Completed" ||
    m.status === "Partial" ||
    m.status === "Failed" ||
    m.status === "Cancelled"
  )
    return { to: `/migrations/${m.id}/results`, label: "Results" };
  return { to: `/migrations/${m.id}/run`, label: "View" };
}

function pct(m: MigrationDto): number {
  const p = m.progress;
  if (!p) return 0;
  if (typeof p.percent === "number") return Math.round(p.percent);
  return p.total > 0 ? Math.round((p.migrated / p.total) * 100) : 0;
}

function Welcome() {
  const navigate = useNavigate();
  return (
    <div className="mx-auto max-w-[560px] py-24 text-center">
      <div className="mx-auto mb-6 flex h-14 w-14 items-center justify-center rounded-2xl bg-accent-subtle text-accent">
        <ArrowRight size={26} aria-hidden />
      </div>
      <h2 className="text-[length:var(--fs-display)] font-semibold tracking-tight">Move your email, safely.</h2>
      <p className="mx-auto mt-3 max-w-[44ch] text-fg-muted">
        EMaigrator copies a mailbox from one provider to another — your source is never changed.
      </p>
      <button
        type="button"
        onClick={() => navigate("/migrations/new")}
        className={`mt-8 ${buttonVariants({ size: "lg" })}`}
      >
        Start your first migration
      </button>
    </div>
  );
}

export function Dashboard() {
  const [items, setItems] = useState<MigrationDto[] | null>(null);
  const [layout, setLayout] = useState<Layout>(
    (localStorage.getItem("em-dash-layout") as Layout) ?? "cards",
  );

  useEffect(() => {
    // On 401 the client redirects to /login; clear the loading state so we never hang on the skeleton.
    void listMigrations()
      .then(setItems)
      .catch(() => setItems([]));
  }, []);

  // Live per-row updates: a SINGLE hub client subscribed to every in-flight row. Progress events
  // carry the migrationId (wire MigrationProgressDto.MigrationId) so we can route them to the right
  // row; StatusChanged already carries the id. Re-subscribes only when the live-id set changes.
  const liveIds = (items ?? []).filter((m) => isNonTerminal(m.status)).map((m) => m.id);
  const liveKey = liveIds.slice().sort().join(",");
  useEffect(() => {
    if (!liveKey) return;
    const ids = liveKey.split(",");
    let client: MigrationsHubClient;
    try {
      client = new MigrationsHubClient();
    } catch {
      return; // realtime unavailable (e.g. no hub endpoint) — render without live updates
    }
    const offs = [
      client.onProgress((dto) => {
        if (!dto.migrationId) return;
        setItems((prev) =>
          prev?.map((m) =>
            m.id === dto.migrationId
              ? { ...m, progress: dto, status: dto.status ?? m.status }
              : m,
          ) ?? prev,
        );
      }),
      client.onStatusChanged((id, status) => {
        setItems((prev) =>
          prev?.map((m) => (m.id === id ? { ...m, status: status as JobStatus } : m)) ?? prev,
        );
      }),
    ];
    let cancelled = false;
    void (async () => {
      try {
        await client.start();
        for (const id of ids) {
          if (cancelled) return;
          await client.subscribe(id);
        }
      } catch {
        /* transient realtime failure — the table still shows the last server snapshot */
      }
    })();
    return () => {
      cancelled = true;
      offs.forEach((off) => off());
      for (const id of ids) void client.unsubscribe(id).catch(() => {});
      void client.stop().catch(() => {});
    };
  }, [liveKey]);

  function setAndPersist(l: Layout) {
    setLayout(l);
    localStorage.setItem("em-dash-layout", l);
  }

  if (items === null) {
    return (
      <div
        role="status"
        aria-label="Loading migrations"
        className="h-24 animate-pulse rounded bg-surface-2"
      />
    );
  }
  if (items.length === 0) return <Welcome />;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <p className="text-sm text-fg-muted">
          {items.length} migration{items.length === 1 ? "" : "s"}
        </p>
        <div className="flex rounded-[var(--radius)] border border-border" role="group" aria-label="Layout density">
          <button
            type="button"
            aria-label="Cards view"
            aria-pressed={layout === "cards"}
            onClick={() => setAndPersist("cards")}
            className={`flex h-8 w-9 items-center justify-center rounded-l-[5px] ${layout === "cards" ? "bg-accent-subtle text-accent" : "text-fg-muted hover:text-fg"}`}
          >
            <LayoutGrid size={16} aria-hidden />
          </button>
          <button
            type="button"
            aria-label="List view"
            aria-pressed={layout === "list"}
            onClick={() => setAndPersist("list")}
            className={`flex h-8 w-9 items-center justify-center rounded-r-[5px] ${layout === "list" ? "bg-accent-subtle text-accent" : "text-fg-muted hover:text-fg"}`}
          >
            <List size={16} aria-hidden />
          </button>
        </div>
      </div>

      {layout === "cards" ? (
        <ul className="grid gap-[var(--grid-gap)] md:grid-cols-2">
          {items.map((m) => {
            const a = actionFor(m);
            const chip = jobStatusToChip(m.status);
            return (
              <li
                key={m.id}
                className="relative flex flex-col gap-3 overflow-hidden rounded-[var(--radius)] border border-border bg-surface-raised p-[var(--card-pad)] shadow-sm transition-colors hover:border-border-strong"
              >
                <span aria-hidden className={`absolute inset-y-0 left-0 w-1 ${ACCENT_BAR[chip]}`} />
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <ProviderRoute from={m.from} to={m.to} />
                    <div className="mt-1 text-sm text-fg-muted">
                      {m.scopeSummary ?? `${m.mailboxCount} mailboxes`}
                    </div>
                  </div>
                  <StatusChip status={chip} />
                </div>
                {m.progress ? (
                  <div className="space-y-1.5">
                    <ProgressBar value={pct(m)} label="Migration progress" />
                    <div className="flex justify-between text-xs text-fg-muted">
                      <span className="mono">{pct(m)}%</span>
                      {m.progress.msgPerMin ? <span className="mono">{m.progress.msgPerMin} msg/min</span> : null}
                    </div>
                  </div>
                ) : null}
                <div className="mt-auto flex justify-end pt-1">
                  <Link
                    to={a.to}
                    className={buttonVariants({ variant: a.label === "Resume" ? "default" : "outline", size: "sm" })}
                  >
                    {a.label}
                  </Link>
                </div>
              </li>
            );
          })}
        </ul>
      ) : (
        <ul className="divide-y divide-border rounded-[var(--radius)] border border-border">
          {items.map((m) => {
            const a = actionFor(m);
            return (
              <li key={m.id} className="flex items-center justify-between gap-4 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <ProviderRoute from={m.from} to={m.to} />
                  <div className="text-sm text-fg-muted">
                    {m.scopeSummary ?? `${m.mailboxCount} mailboxes`}
                  </div>
                </div>
                {m.progress ? <span className="mono w-12 text-right text-sm text-fg-muted">{pct(m)}%</span> : null}
                <StatusChip status={jobStatusToChip(m.status)} />
                <Link to={a.to} className={buttonVariants({ variant: "ghost", size: "sm" })}>
                  {a.label}
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
