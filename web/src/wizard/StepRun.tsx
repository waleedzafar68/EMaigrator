import { lazy, Suspense, useEffect, useRef, useState } from "react";
import { useOutletContext } from "react-router-dom";
import { Folder, Gauge, Pause, Play, Timer, X } from "lucide-react";
import type { MigrationDto } from "../api/types";
import { cancel, pause, resume } from "../api/migrations";
import { useMigrationStream } from "../api/useMigrationStream";
import { ProgressBar } from "../components/ProgressBar";
import { ProviderRoute } from "../components/ProviderRoute";
import { StatusChip } from "../components/StatusChip";
import { Button } from "../components/ui/button";

const ThroughputChart = lazy(() => import("./ThroughputChart"));

function formatEta(seconds: number): string {
  const mins = Math.round(seconds / 60);
  if (mins < 1) return "<1m left";
  if (mins < 60) return `~${mins}m left`;
  const h = Math.floor(mins / 60);
  return `~${h}h ${mins % 60}m left`;
}

function StatTile({ icon, label, value }: { icon: React.ReactNode; label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center gap-2.5 rounded-[var(--radius)] border border-border bg-surface px-3 py-2.5">
      <span className="text-fg-subtle">{icon}</span>
      <span className="min-w-0">
        <span className="block text-xs text-fg-muted">{label}</span>
        <span className="mono block truncate text-sm text-fg">{value}</span>
      </span>
    </div>
  );
}

export function StepRun() {
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const { progress, connectionState, status } = useMigrationStream(migration.id);
  const [dense, setDense] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);

  // Accumulate a small rolling window of throughput samples to draw a sparkline.
  const [samples, setSamples] = useState<{ t: number; rate: number }[]>([]);
  const tick = useRef(0);
  useEffect(() => {
    const rate = progress?.msgPerMin;
    if (typeof rate !== "number") return;
    setSamples((s) => [...s, { t: tick.current++, rate }].slice(-30));
  }, [progress?.msgPerMin, progress?.migrated]);

  const pct = !progress
    ? 0
    : typeof progress.percent === "number"
      ? Math.round(progress.percent)
      : progress.total > 0
        ? Math.round((progress.migrated / progress.total) * 100)
        : 0;
  // Throttling is a dedicated flag, not a JobStatus value (see MigrationProgressDto).
  const throttled = progress?.throttled === true;
  const isPaused = status === "Paused";
  const isTerminal = status === "Completed" || status === "Partial" || status === "Failed" || status === "Cancelled";

  const migrated = progress?.migrated ?? 0;
  const total = progress?.total ?? 0;
  const rate = progress?.msgPerMin ?? 0;
  const remaining = Math.max(0, total - migrated);
  const showEta = !isTerminal && !isPaused && rate > 0 && remaining > 0;
  const eta = showEta ? formatEta((remaining / rate) * 60) : null;

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-[length:var(--fs-h1)] font-semibold">Migrating</h2>
          {migration.from && migration.to ? (
            <div className="mt-1 text-sm text-fg-muted"><ProviderRoute from={migration.from} to={migration.to} /></div>
          ) : null}
        </div>
        {connectionState === "reconnecting" ? (
          <span role="status" className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
            <span className="h-2 w-2 animate-pulse rounded-full bg-throttled" aria-hidden /> Reconnecting…
          </span>
        ) : null}
      </div>

      <div className="rounded-[var(--radius)] border border-border bg-surface-raised p-[var(--card-pad)] shadow-sm">
        <div className="flex items-end justify-between">
          <span className="mono text-[length:var(--fs-display)] font-semibold leading-none">{pct}%</span>
          <span className="mono text-sm text-fg-muted">{migrated.toLocaleString()} / {total.toLocaleString()}</span>
        </div>
        <div className="mt-3">
          <ProgressBar value={pct} label="Migration progress" />
        </div>
        {throttled ? <div className="mt-3"><StatusChip status="throttled" /></div> : null}
      </div>

      <div className="grid gap-[var(--grid-gap)] sm:grid-cols-3">
        <StatTile icon={<Gauge size={16} aria-hidden />} label="Throughput" value={`${rate} msg/min`} />
        <StatTile icon={<Timer size={16} aria-hidden />} label="Remaining" value={eta ?? (isTerminal ? "Done" : "—")} />
        <StatTile icon={<Folder size={16} aria-hidden />} label="Current folder" value={progress?.currentFolder ?? "—"} />
      </div>

      {samples.length >= 2 ? (
        <div className="rounded-[var(--radius)] border border-border bg-surface-raised p-3">
          <div className="mb-1 text-xs text-fg-muted">Throughput (msg/min)</div>
          <Suspense fallback={<div className="h-16 w-full" aria-hidden />}>
            <ThroughputChart samples={samples} />
          </Suspense>
        </div>
      ) : null}

      {!isTerminal ? (
        <div className="flex gap-2">
          {isPaused ? (
            <Button type="button" variant="outline" onClick={() => void resume(migration.id)}>
              <Play size={16} aria-hidden /> Resume
            </Button>
          ) : (
            <Button type="button" variant="outline" onClick={() => void pause(migration.id)}>
              <Pause size={16} aria-hidden /> Pause
            </Button>
          )}
          <Button
            type="button"
            variant={confirmingCancel ? "destructive" : "outline"}
            onClick={() => { if (confirmingCancel) void cancel(migration.id); else setConfirmingCancel(true); }}
          >
            <X size={16} aria-hidden /> {confirmingCancel ? "Click again to confirm cancel" : "Cancel"}
          </Button>
        </div>
      ) : null}

      {migration.isBatch ? (
        <button type="button" className="text-sm text-accent hover:underline" aria-pressed={dense} onClick={() => setDense((d) => !d)}>
          {dense ? "Simple view" : "Detailed view"}
        </button>
      ) : null}
      {migration.isBatch && dense ? (
        <p className="text-sm text-fg-muted">Per-mailbox detail isn't available yet — showing overall progress.</p>
      ) : null}

      <p className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
        <Timer size={14} aria-hidden /> Safe to close — runs in the background.
      </p>
    </div>
  );
}
