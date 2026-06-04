import { useState } from "react";
import { useOutletContext } from "react-router-dom";
import type { MigrationDto } from "../api/types";
import { cancel, pause, resume } from "../api/migrations";
import { useMigrationStream } from "../api/useMigrationStream";
import { ProgressBar } from "../components/ProgressBar";
import { StatusChip } from "../components/StatusChip";

export function StepRun() {
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const { progress, connectionState, status } = useMigrationStream(migration.id);
  const [dense, setDense] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);

  const pct = progress && progress.total > 0 ? Math.round((progress.migratedCount / progress.total) * 100) : 0;
  // Throttling is a dedicated flag, not a JobStatus value (see MigrationProgressDto).
  const throttled = progress?.throttled === true;
  const isPaused = status === "Paused";
  const isTerminal = status === "Completed" || status === "Partial" || status === "Failed" || status === "Cancelled";

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-[length:var(--fs-h1)] font-semibold">Migrating</h2>
        {connectionState === "reconnecting" ? (
          <span role="status" className="text-sm text-fg-muted">Reconnecting…</span>
        ) : null}
      </div>

      <ProgressBar value={pct} label="Migration progress" />
      <p className="mono text-sm">
        {(progress?.migratedCount ?? 0).toLocaleString()} / {(progress?.total ?? 0).toLocaleString()}
      </p>
      {progress?.currentFolder ? <p className="text-sm text-fg-muted">Current: {progress.currentFolder}</p> : null}
      <p className="mono text-sm">{progress?.msgPerMin ?? 0} msg/min</p>
      {throttled ? <StatusChip status="throttled" /> : null}

      {!isTerminal ? (
        <div className="flex gap-2">
          {isPaused ? (
            <button type="button" onClick={() => void resume(migration.id)} className="rounded-[8px] border border-border px-3 py-1.5">Resume</button>
          ) : (
            <button type="button" onClick={() => void pause(migration.id)} className="rounded-[8px] border border-border px-3 py-1.5">⏸ Pause</button>
          )}
          <button type="button"
            onClick={() => { if (confirmingCancel) void cancel(migration.id); else setConfirmingCancel(true); }}
            className={`rounded-[8px] border px-3 py-1.5 ${confirmingCancel ? "border-error text-error" : "border-border"}`}>
            {confirmingCancel ? "Click again to confirm cancel" : "✕ Cancel"}
          </button>
        </div>
      ) : null}

      {migration.isBatch ? (
        <button type="button" className="text-sm text-accent" aria-pressed={dense} onClick={() => setDense((d) => !d)}>
          {dense ? "Simple view" : "Detailed view"}
        </button>
      ) : null}
      {migration.isBatch && dense ? (
        <p className="text-sm text-fg-muted">Per-mailbox detail isn't available yet — showing overall progress.</p>
      ) : null}

      <p className="text-sm text-fg-muted">🔒 Safe to close — runs in the background.</p>
    </div>
  );
}
