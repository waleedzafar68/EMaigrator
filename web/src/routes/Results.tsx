import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { AlertTriangle, Check, Download, Lock, MinusCircle, RefreshCw, Wrench } from "lucide-react";
import type { AuditEntryDto, ResultsDto } from "../api/types";
import { getAudit, getResults, reconcile, rerun, reportUrl } from "../api/migrations";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { ErrorAlert } from "../components/ErrorAlert";
import { errorAlertProps } from "../components/states/fromApiError";
import { formatElapsed } from "../wizard/format";
import { AuditTable } from "./AuditTable";

// Prefer the job's real status (API ResultsDto.Status) for the header; fall back to a reconciliation-
// derived label when it is somehow absent (e.g. an older API). A "Completed" job reads as complete;
// any other terminal/outstanding status surfaces in the header so the operator sees the true outcome.
function resultsHeader(data: ResultsDto): string {
  if (data.status) {
    return data.status === "Completed" ? "Migration complete" : `Migration complete — ${data.status}`;
  }
  const clean = data.reconciliation.matched && data.counts.failed === 0;
  return clean ? "Migration complete" : "Migration complete — Partial";
}

function Stat({ icon, value, label, cls }: { icon: React.ReactNode; value: string; label: string; cls: string }) {
  return (
    <div className="flex items-center gap-2.5 rounded-[var(--radius)] border border-border bg-surface-raised px-3.5 py-2.5">
      <span className={cls}>{icon}</span>
      <span>
        <span className="mono block text-base font-semibold text-fg">{value}</span>
        <span className="block text-xs text-fg-muted">{label}</span>
      </span>
    </div>
  );
}

export function Results() {
  const { id = "" } = useParams();
  const [data, setData] = useState<ResultsDto | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [audit, setAudit] = useState<AuditEntryDto[]>([]);
  const [failuresOnly, setFailuresOnly] = useState(false);
  const [q, setQ] = useState("");
  const [debouncedQ, setDebouncedQ] = useState("");
  const [reconciling, setReconciling] = useState(false);
  const [reconcileError, setReconcileError] = useState<unknown>(null);
  const [reconcileStarted, setReconcileStarted] = useState(false);

  const onReconcile = () => {
    setReconcileError(null);
    setReconciling(true);
    void reconcile(id)
      .then(() => setReconcileStarted(true))
      .catch((e: unknown) => setReconcileError(e)) // 401 redirects globally; show anything else
      .finally(() => setReconciling(false));
  };

  useEffect(() => {
    const t = setTimeout(() => setDebouncedQ(q), 300);
    return () => clearTimeout(t);
  }, [q]);

  useEffect(() => {
    let active = true;
    void getResults(id)
      .then((r) => { if (active) setData(r); })
      .catch((e: unknown) => { if (active) setError(e); }); // 401 redirects globally; show anything else
    return () => { active = false; };
  }, [id]);

  useEffect(() => {
    let active = true;
    void getAudit(id, { q: debouncedQ, failuresOnly }).then((rows) => { if (active) setAudit(rows); }).catch(() => {});
    return () => { active = false; };
  }, [id, debouncedQ, failuresOnly]);

  if (error) return <ErrorAlert {...errorAlertProps(error)} />;
  if (!data) return <div role="status" aria-label="Loading results" className="h-24 animate-pulse rounded bg-surface-2" />;

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-[length:var(--fs-h1)] font-semibold">{resultsHeader(data)}</h2>
        <p className="mt-1 text-sm text-fg-muted">
          {data.reconciliation.sourceCount.toLocaleString()} in source, {data.reconciliation.destCount.toLocaleString()} in destination
          {data.reconciliation.matched ? <Check size={14} aria-hidden className="ml-1 inline text-success" /> : null}
          {data.durationSeconds != null ? <span className="mono"> · Took {formatElapsed(data.durationSeconds)}</span> : null}
        </p>
      </div>

      <div className="grid gap-[var(--grid-gap)] sm:grid-cols-3">
        <Stat icon={<Check size={18} aria-hidden />} cls="text-success" value={data.counts.migrated.toLocaleString()} label="migrated" />
        <Stat icon={<AlertTriangle size={18} aria-hidden />} cls="text-warning" value={String(data.needsDecision.length)} label="need your decision" />
        <Stat icon={<MinusCircle size={18} aria-hidden />} cls="text-fg-muted" value={String(data.counts.skipped)} label="skipped" />
      </div>

      <div className="space-y-2 rounded-[var(--radius)] border border-border bg-surface-raised p-4">
        <h3 className="flex items-center gap-2 font-medium"><Wrench size={16} aria-hidden className="text-accent" /> Reconcile / repair</h3>
        <p className="text-sm text-fg-muted">
          Re-scan the destination and copy any messages still missing + backfill missing attachments. Non-destructive · idempotent.
        </p>
        {reconcileStarted ? (
          <p role="status" className="inline-flex items-center gap-1.5 text-sm text-accent"><RefreshCw size={14} aria-hidden className="animate-spin" /> Reconcile started — now running.</p>
        ) : (
          <Button type="button" onClick={onReconcile} disabled={reconciling}>
            {reconciling ? "Reconciling…" : "Reconcile / repair"}
          </Button>
        )}
        {reconcileError ? <ErrorAlert {...errorAlertProps(reconcileError)} /> : null}
      </div>

      {data.needsDecision.length ? (
        <div className="space-y-2 rounded-[var(--radius)] border border-warning-line bg-warning-bg p-4">
          <h3 className="flex items-center gap-2 font-medium"><AlertTriangle size={16} aria-hidden className="text-warning" /> Needs your decision ({data.needsDecision.length})</h3>
          <ul className="space-y-1 text-sm">
            {data.needsDecision.map((n, i) => (
              <li key={i} className="flex items-center justify-between gap-3">
                <span>{n.detail}</span>
                <button type="button" disabled title="Coming in a future release" className="text-accent opacity-50">Resolve</button>
              </li>
            ))}
          </ul>
          <div className="flex items-center gap-2 pt-1">
            <Button type="button" variant="outline" size="sm" onClick={() => void rerun(id)}>
              <RefreshCw size={14} aria-hidden /> Re-run unfinished items
            </Button>
            <span className="text-sm text-fg-muted">idempotent · free</span>
          </div>
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-3">
        <Input aria-label="Search audit" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search" className="h-9 w-56" />
        <label className="flex items-center gap-1.5 text-sm">
          <input type="checkbox" checked={failuresOnly} onChange={(e) => setFailuresOnly(e.target.checked)} className="accent-[var(--accent)]" /> Failures only
        </label>
        <a href={reportUrl(id, "csv")} className="ml-auto inline-flex items-center gap-1.5 text-accent hover:underline"><Download size={14} aria-hidden /> Export CSV</a>
        <a href={reportUrl(id, "pdf")} className="inline-flex items-center gap-1.5 text-accent hover:underline"><Download size={14} aria-hidden /> Export PDF</a>
      </div>

      <AuditTable entries={audit} />

      {data.logDeletesAt ? (
        <p className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
          <Lock size={14} aria-hidden /> This log auto-deletes on {new Date(data.logDeletesAt).toLocaleDateString()}. <button type="button" disabled title="Coming in a future release" className="text-accent opacity-50">Delete now</button>
        </p>
      ) : null}
    </div>
  );
}
