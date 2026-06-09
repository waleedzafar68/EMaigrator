import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import type { MailboxPairDto, MigrationDto, ScopeRequest } from "../api/types";
import { useDraft } from "./useDraft";
import { parsePairsCsv } from "./csv";

interface ScopeCtx { migration: MigrationDto; canBatch: boolean; }

export function StepScope() {
  const { migration, canBatch } = useOutletContext<ScopeCtx>();
  const { saveScope } = useDraft(migration.id);
  const navigate = useNavigate();
  const [isBatch, setIsBatch] = useState(false);
  const [pairs, setPairs] = useState<MailboxPairDto[]>([]);
  const [csvErrors, setCsvErrors] = useState<string[]>([]);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [newSource, setNewSource] = useState("");
  const [newDest, setNewDest] = useState("");
  const [since, setSince] = useState("");

  async function onCsv(file: File) {
    const text = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as string);
      reader.onerror = () => reject(reader.error);
      reader.readAsText(file);
    });
    const { pairs: p, errors } = parsePairsCsv(text);
    setPairs(p);
    setCsvErrors(errors);
  }

  async function onContinue() {
    const scope: ScopeRequest = { isBatch, pairs };
    // A date-only input is widened to an explicit UTC instant so the API's DateTimeOffset binder accepts it.
    if (since) scope.since = new Date(`${since}T00:00:00Z`).toISOString();
    await saveScope(scope);
    navigate(`/migrations/${migration.id}/review`);
  }

  return (
    <div className="space-y-5">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">What should we migrate?</h2>

      <div role="group" aria-label="Scope mode" className="flex gap-2">
        <button type="button" aria-pressed={!isBatch} onClick={() => setIsBatch(false)}
          className={`rounded-[6px] border px-3 py-1.5 ${!isBatch ? "border-accent" : "border-border"}`}>Single</button>
        <button type="button" aria-pressed={isBatch} disabled={!canBatch} onClick={() => setIsBatch(true)}
          className={`rounded-[6px] border px-3 py-1.5 disabled:opacity-40 ${isBatch ? "border-accent" : "border-border"}`}>Batch</button>
      </div>
      {!canBatch ? (
        <p className="text-sm text-fg-muted">To migrate multiple mailboxes, reconnect using admin access.</p>
      ) : null}

      {isBatch ? (
        <div className="space-y-3">
          <label className="block text-sm">Import CSV (source_mailbox, destination_mailbox)
            <input aria-label="Import CSV" type="file" accept=".csv,text/csv"
              onChange={(e) => e.target.files?.[0] && void onCsv(e.target.files[0])} className="mt-1 block" />
          </label>
          <div className="flex items-end gap-2">
            <label className="text-sm">Source mailbox
              <input aria-label="New source mailbox" value={newSource} onChange={(e) => setNewSource(e.target.value)}
                className="mt-1 block rounded-[6px] border border-border-strong px-2 py-1" />
            </label>
            <label className="text-sm">Destination mailbox
              <input aria-label="New destination mailbox" value={newDest} onChange={(e) => setNewDest(e.target.value)}
                className="mt-1 block rounded-[6px] border border-border-strong px-2 py-1" />
            </label>
            <button type="button" className="rounded-[6px] border border-border px-3 py-1.5 text-sm"
              onClick={() => {
                const s = newSource.trim();
                const d = newDest.trim();
                if (!s || !d) return;
                setPairs((p) => [...p, { sourceMailbox: s, destMailbox: d }]);
                setNewSource("");
                setNewDest("");
              }}>
              Add pair
            </button>
          </div>
          {csvErrors.length ? <ul className="text-sm text-error">{csvErrors.map((e) => <li key={e}>{e}</li>)}</ul> : null}
          {pairs.length ? (
            <table className="w-full text-sm"><thead><tr className="text-left text-fg-muted"><th>From</th><th>To</th><th>Status</th></tr></thead>
              <tbody>{pairs.map((p, i) => (
                <tr key={i} className="border-t border-border">
                  <td className="mono">{p.sourceMailbox}</td>
                  <td className="mono">{p.destMailbox}</td>
                  <td>{p.sourceMailbox && p.destMailbox ? "✓ valid" : "⚠ incomplete"}</td>
                </tr>
              ))}</tbody>
            </table>
          ) : null}
        </div>
      ) : (
        <p className="text-fg-muted">Migrating one mailbox. Confirm and continue.</p>
      )}

      <label className="block text-sm">Only mail since (optional — limits a migrate or reconcile to a recent window)
        <input aria-label="Since date" type="date" value={since} onChange={(e) => setSince(e.target.value)}
          className="mt-1 block h-[var(--control-h)] rounded-[6px] border border-border-strong px-2" />
      </label>

      <button type="button" className="text-sm text-fg-muted" aria-expanded={showAdvanced}
        onClick={() => setShowAdvanced((s) => !s)}>▸ Advanced</button>
      {showAdvanced ? (
        <div className="space-y-2">
          <label className="block text-sm">Include folders<input aria-label="Include folders" className="mt-1 block w-full rounded-[6px] border border-border-strong px-2 py-1" /></label>
          <label className="block text-sm">Exclude folders<input aria-label="Exclude folders" className="mt-1 block w-full rounded-[6px] border border-border-strong px-2 py-1" /></label>
        </div>
      ) : null}

      <button type="button" onClick={() => void onContinue()} className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg">Continue</button>
    </div>
  );
}
