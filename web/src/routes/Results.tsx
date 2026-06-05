import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import type { AuditEntryDto, ResultsDto } from "../api/types";
import { getAudit, getResults, rerun, reportUrl } from "../api/migrations";
import { ErrorAlert } from "../components/ErrorAlert";
import { errorAlertProps } from "../components/states/fromApiError";
import { AuditTable } from "./AuditTable";

// The API ResultsDto does NOT carry a status, so the header is derived from reconciliation: a fully
// matched, failure-free job reads "complete"; anything outstanding reads "Partial".
function resultsHeader(data: ResultsDto): string {
  const clean = data.reconciliation.matched && data.counts.failed === 0;
  return clean ? "Migration complete" : "Migration complete — Partial";
}

export function Results() {
  const { id = "" } = useParams();
  const [data, setData] = useState<ResultsDto | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [audit, setAudit] = useState<AuditEntryDto[]>([]);
  const [failuresOnly, setFailuresOnly] = useState(false);
  const [q, setQ] = useState("");
  const [debouncedQ, setDebouncedQ] = useState("");

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
    <div className="space-y-5">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">{resultsHeader(data)}</h2>
      <p className="mono text-sm">
        ✓ {data.counts.migrated.toLocaleString()} migrated · ⚠ {data.needsDecision.length} need your decision · ⤫ {data.counts.skipped} skipped
      </p>
      <p className="text-sm text-fg-muted">
        {data.reconciliation.sourceCount.toLocaleString()} in source, {data.reconciliation.destCount.toLocaleString()} in destination
        {data.reconciliation.matched ? " ✓" : ""}
      </p>

      {data.needsDecision.length ? (
        <div className="space-y-2 rounded-[6px] border border-warning p-3">
          <h3 className="font-medium">Needs your decision ({data.needsDecision.length})</h3>
          <ul className="space-y-1 text-sm">
            {data.needsDecision.map((n, i) => (
              <li key={i} className="flex items-center justify-between">
                <span>{n.detail}</span>
                <button type="button" disabled title="Coming in a future release" className="text-accent opacity-50">Resolve</button>
              </li>
            ))}
          </ul>
          <button type="button" onClick={() => void rerun(id)} className="rounded-[8px] bg-accent px-3 py-1.5 text-accent-fg">
            Re-run unfinished items
          </button>
          <span className="ml-2 text-sm text-fg-muted">idempotent · free</span>
        </div>
      ) : null}

      <div className="flex items-center gap-3">
        <input aria-label="Search audit" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search"
          className="h-[var(--control-h)] rounded-[6px] border border-border-strong px-2 text-sm" />
        <label className="flex items-center gap-1 text-sm">
          <input type="checkbox" checked={failuresOnly} onChange={(e) => setFailuresOnly(e.target.checked)} /> Failures only
        </label>
        <a href={reportUrl(id, "csv")} className="text-accent">Export CSV</a>
        <a href={reportUrl(id, "pdf")} className="text-accent">Export PDF</a>
      </div>

      <AuditTable entries={audit} />

      <p className="text-sm text-fg-muted">
        🔒 This log auto-deletes in 30 days. <button type="button" disabled title="Coming in a future release" className="text-accent opacity-50">Delete now</button>
      </p>
    </div>
  );
}
