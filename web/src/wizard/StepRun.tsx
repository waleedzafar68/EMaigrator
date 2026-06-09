import { lazy, Suspense, useEffect, useMemo, useRef, useState } from "react";
import { Link, useOutletContext } from "react-router-dom";
import { AlertTriangle, CheckCheck, Copy, Folder, Gauge, Loader2, Paperclip, Pause, Play, ShieldCheck, Timer, X } from "lucide-react";
import type { MigrationDto, MigrationProgressDto } from "../api/types";
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
  const isReconcile = (migration.mode ?? "migrate") === "reconcile";
  const { progress: streamProgress, connectionState, status } = useMigrationStream(migration.id);
  const [dense, setDense] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);

  // Progress lives only in the live SignalR stream, so a page refresh used to reset the view to
  // 0% until the NEXT event arrived (a long wait on big reconciles). Cache the last progress +
  // activity per migration in sessionStorage — counts and folder names only, never message
  // content — and rehydrate on mount so the view survives reloads.
  const storageKey = `em-run:${migration.id}`;
  const cached = useMemo(() => {
    try {
      return JSON.parse(sessionStorage.getItem(storageKey) ?? "null") as
        { progress: MigrationProgressDto; activity: string[] } | null;
    } catch {
      return null;
    }
  }, [storageKey]);
  const progress = streamProgress ?? cached?.progress ?? null;

  // Accumulate a small rolling window of throughput samples to draw a sparkline.
  const [samples, setSamples] = useState<{ t: number; rate: number }[]>([]);
  const tick = useRef(0);
  useEffect(() => {
    const rate = progress?.msgPerMin;
    if (typeof rate !== "number") return;
    setSamples((s) => [...s, { t: tick.current++, rate }].slice(-30));
  }, [progress?.msgPerMin, progress?.migrated]);

  // Reconcile activity feed: one line per folder advance (keyed on the current folder changing).
  // Seeded from the cache so the rehydrated head folder isn't re-appended.
  const [activity, setActivity] = useState<string[]>(() => cached?.activity ?? []);
  const lastFolder = useRef<string | null>(cached?.activity?.[0] ?? null);
  useEffect(() => {
    const f = progress?.currentFolder;
    if (!isReconcile || !f || f === lastFolder.current) return;
    lastFolder.current = f;
    setActivity((a) => [f, ...a].slice(0, 50));
  }, [isReconcile, progress?.currentFolder]);

  // Persist on every live event (best-effort: a full/blocked storage only loses refresh-survival).
  useEffect(() => {
    if (!streamProgress) return;
    try {
      sessionStorage.setItem(storageKey, JSON.stringify({ progress: streamProgress, activity }));
    } catch {
      // ignore — live view still works without the cache
    }
  }, [streamProgress, activity, storageKey]);

  const pct = !progress
    ? 0
    : typeof progress.percent === "number"
      ? Math.round(progress.percent)
      : progress.total > 0
        ? Math.round((progress.migrated / progress.total) * 100)
        : 0;
  // Throttling is a dedicated flag, not a JobStatus value (see MigrationProgressDto).
  const throttled = progress?.throttled === true;
  // After the run no further events arrive, so a reloaded page must learn "done" from the cached
  // last event or the REST migration status — the live stream alone would leave it looking active.
  const effStatus = status ?? progress?.status ?? migration.status ?? null;
  const isPaused = effStatus === "Paused";
  const isTerminal =
    effStatus === "Completed" || effStatus === "Partial" || effStatus === "Failed" || effStatus === "Cancelled";

  const migrated = progress?.migrated ?? 0;
  const total = progress?.total ?? 0;
  const rate = progress?.msgPerMin ?? 0;
  const remaining = Math.max(0, total - migrated);
  const showEta = !isTerminal && !isPaused && rate > 0 && remaining > 0;
  const eta = showEta ? formatEta((remaining / rate) * 60) : null;

  // Shared pause/resume/cancel controls (only while the run is live).
  const controls = !isTerminal ? (
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
  ) : null;

  if (isReconcile) {
    const rc = progress?.reconcile ?? null;
    const folderPct = rc && rc.folderTotal > 0 ? Math.round((rc.foldersDone / rc.folderTotal) * 100) : 0;
    return (
      <div className="space-y-5">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="text-[length:var(--fs-h1)] font-semibold">Reconciling</h2>
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

        {isTerminal ? (
          <div className="flex items-start gap-2.5 rounded-[var(--radius)] border border-border bg-surface-raised px-4 py-3 text-sm">
            {effStatus === "Completed" ? (
              <CheckCheck size={16} aria-hidden className="mt-0.5 text-success" />
            ) : (
              <AlertTriangle size={16} aria-hidden className="mt-0.5 text-warning" />
            )}
            <div className="space-y-1">
              <p className="font-medium text-fg">
                {effStatus === "Completed"
                  ? "Reconcile complete — the destination matches the source."
                  : `Reconcile finished — ${effStatus}. Some messages need attention.`}
              </p>
              <Link to={`/migrations/${migration.id}/results`} className="text-accent hover:underline">
                View results
              </Link>
            </div>
          </div>
        ) : null}

        {rc || !isTerminal ? (
          <div className="rounded-[var(--radius)] border border-border bg-surface-raised p-[var(--card-pad)] shadow-sm">
            {rc ? (
              <>
                <div className="flex items-end justify-between">
                  <span className="text-sm font-medium text-fg">
                    Folder {rc.foldersDone.toLocaleString()} of ~{rc.folderTotal.toLocaleString()}
                  </span>
                  <span className="mono text-sm text-fg-muted">{folderPct}%</span>
                </div>
                <div className="mt-3">
                  <ProgressBar value={folderPct} label="Reconcile progress" />
                </div>
              </>
            ) : (
              <div role="status" className="flex items-center gap-2.5 text-sm text-fg-muted">
                <Loader2 size={15} aria-hidden className="animate-spin" />
                Scanning folders — live counts appear as each folder completes…
              </div>
            )}
          </div>
        ) : null}

        {rc ? (
          <div className="grid gap-[var(--grid-gap)] sm:grid-cols-2 lg:grid-cols-4">
            <StatTile icon={<Copy size={16} aria-hidden />} label="Copied" value={rc.copied.toLocaleString()} />
            <StatTile icon={<Paperclip size={16} aria-hidden />} label="Attachments backfilled" value={rc.backfilled.toLocaleString()} />
            <StatTile icon={<CheckCheck size={16} aria-hidden />} label="Already complete (skipped)" value={rc.skipped.toLocaleString()} />
            <StatTile icon={<Gauge size={16} aria-hidden />} label="Throughput" value={`${rate} msg/min`} />
          </div>
        ) : null}

        {activity.length ? (
          <div className="rounded-[var(--radius)] border border-border bg-surface-raised p-3">
            <div className="mb-2 text-xs text-fg-muted">Activity</div>
            {/* Fixed-height scroll region so a long run never pushes the controls off-screen. */}
            <ul className="max-h-48 space-y-1 overflow-y-auto pr-1 text-sm">
              {activity.map((f, i) => (
                <li key={`${f}-${i}`} className="flex items-center gap-2 text-fg-muted">
                  <Folder size={13} aria-hidden className="text-fg-subtle" />
                  <span className="mono truncate text-fg">{f}</span>
                </li>
              ))}
            </ul>
          </div>
        ) : null}

        {controls}

        <p className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
          <ShieldCheck size={14} aria-hidden /> Non-destructive · never duplicates · safe to close — runs in the background.
        </p>
      </div>
    );
  }

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
