import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { LayoutGrid, List } from "lucide-react";
import { listMigrations } from "../api/migrations";
import type { MigrationDto, UsageDto } from "../api/types";
import { jobStatusToChip, StatusChip } from "../components/StatusChip";
import { ProviderRoute } from "../components/ProviderRoute";

type Layout = "cards" | "list";

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
  return p && p.total > 0 ? Math.round((p.migratedCount / p.total) * 100) : 0;
}

function Welcome() {
  const navigate = useNavigate();
  return (
    <div className="mx-auto max-w-[560px] py-20 text-center">
      <h2 className="text-[length:var(--fs-display)] font-semibold">Move your email, safely.</h2>
      <p className="mt-3 text-fg-muted">
        EMaigrator copies a mailbox from one provider to another — your source is never changed.
      </p>
      <button
        type="button"
        onClick={() => navigate("/migrations/new")}
        className="mt-8 inline-flex rounded-[8px] bg-accent px-5 py-3 text-accent-fg"
      >
        Start your first migration
      </button>
    </div>
  );
}

export function Dashboard() {
  const [items, setItems] = useState<MigrationDto[] | null>(null);
  const [usage] = useState<UsageDto | null>(null);
  const [layout, setLayout] = useState<Layout>(
    (localStorage.getItem("em-dash-layout") as Layout) ?? "cards",
  );

  useEffect(() => {
    // On 401 the client redirects to /login; clear the loading state so we never hang on the skeleton.
    void listMigrations()
      .then(setItems)
      .catch(() => setItems([]));
  }, []);

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
      <UsageWidget usage={usage} />
      <div className="flex justify-end gap-1" role="group" aria-label="Layout density">
        <button
          type="button"
          aria-label="Cards view"
          aria-pressed={layout === "cards"}
          onClick={() => setAndPersist("cards")}
          className={layout === "cards" ? "text-accent" : "text-fg-muted"}
        >
          <LayoutGrid size={18} aria-hidden />
        </button>
        <button
          type="button"
          aria-label="List view"
          aria-pressed={layout === "list"}
          onClick={() => setAndPersist("list")}
          className={layout === "list" ? "text-accent" : "text-fg-muted"}
        >
          <List size={18} aria-hidden />
        </button>
      </div>
      <ul
        className={
          layout === "cards"
            ? "grid gap-[var(--grid-gap)] md:grid-cols-2"
            : "divide-y divide-border"
        }
      >
        {items.map((m) => {
          const a = actionFor(m);
          return (
            <li
              key={m.id}
              className="flex items-center justify-between gap-4 rounded-[6px] border border-border p-[var(--card-pad)]"
            >
              <div className="min-w-0">
                <ProviderRoute from={m.from} to={m.to} />
                <div className="mt-1 text-sm text-fg-muted">
                  {m.scopeSummary ?? `${m.mailboxCount} mailboxes`}
                </div>
              </div>
              <div className="flex items-center gap-4">
                {m.progress ? <span className="mono text-sm">{pct(m)}%</span> : null}
                <StatusChip status={jobStatusToChip(m.status)} />
                <Link to={a.to} className="text-accent">
                  {a.label}
                </Link>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export function UsageWidget({ usage }: { usage: UsageDto | null }) {
  if (!usage) return null;
  const usagePct =
    usage.quota > 0 ? Math.min(100, Math.round((usage.used / usage.quota) * 100)) : 0;
  return (
    <div className="flex items-center gap-3 rounded-[6px] border border-border p-3 text-sm">
      <span className="text-fg-muted">Usage</span>
      <div className="h-2 w-40 overflow-hidden rounded-full bg-surface-2">
        <div className="h-full bg-accent" style={{ width: `${usagePct}%` }} />
      </div>
      <span className="mono">
        {usage.used} / {usage.quota} mailboxes this month
      </span>
      <Link to="/upgrade" className="ml-auto text-accent">
        Upgrade
      </Link>
    </div>
  );
}
