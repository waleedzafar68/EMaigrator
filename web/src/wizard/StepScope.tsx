import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { AlertTriangle, Check, ChevronRight, User, Users } from "lucide-react";
import type { MailboxPairDto, MigrationDto, ScopeRequest } from "../api/types";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
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
    <div className="space-y-6">
      <div>
        <h2 className="text-[length:var(--fs-h1)] font-semibold">What should we migrate?</h2>
        <p className="mt-1 text-sm text-fg-muted">Choose how many mailboxes this run covers.</p>
      </div>

      <div role="group" aria-label="Scope mode" className="grid grid-cols-2 gap-2 sm:max-w-md">
        <button
          type="button"
          aria-pressed={!isBatch}
          onClick={() => setIsBatch(false)}
          className={`flex items-start gap-2.5 rounded-[var(--radius)] border p-3 text-left transition-colors ${
            !isBatch ? "border-accent bg-accent-subtle ring-1 ring-accent" : "border-border hover:bg-surface-2"
          }`}
        >
          <User size={16} aria-hidden className="mt-0.5 text-accent" />
          <span>
            <span className="block text-sm font-medium">Single</span>
            <span className="block text-xs text-fg-muted">One mailbox</span>
          </span>
        </button>
        <button
          type="button"
          aria-pressed={isBatch}
          disabled={!canBatch}
          onClick={() => setIsBatch(true)}
          className={`flex items-start gap-2.5 rounded-[var(--radius)] border p-3 text-left transition-colors disabled:opacity-40 ${
            isBatch ? "border-accent bg-accent-subtle ring-1 ring-accent" : "border-border hover:bg-surface-2"
          }`}
        >
          <Users size={16} aria-hidden className="mt-0.5 text-accent" />
          <span>
            <span className="block text-sm font-medium">Batch</span>
            <span className="block text-xs text-fg-muted">Many mailboxes (CSV)</span>
          </span>
        </button>
      </div>
      {!canBatch ? (
        <p className="text-sm text-fg-muted">To migrate multiple mailboxes, reconnect using admin access.</p>
      ) : null}

      {isBatch ? (
        <div className="space-y-4">
          <label className="block text-sm font-medium">
            Import CSV <span className="font-normal text-fg-muted">(source_mailbox, destination_mailbox)</span>
            <input aria-label="Import CSV" type="file" accept=".csv,text/csv"
              onChange={(e) => e.target.files?.[0] && void onCsv(e.target.files[0])}
              className="mt-1.5 block w-full text-sm text-fg-muted file:mr-3 file:rounded-[var(--radius-sm)] file:border-0 file:bg-surface-2 file:px-3 file:py-1.5 file:text-sm file:font-medium file:text-fg hover:file:bg-border" />
          </label>
          <div className="flex flex-wrap items-end gap-2">
            <label className="text-sm font-medium">
              Source mailbox
              <Input aria-label="New source mailbox" value={newSource} onChange={(e) => setNewSource(e.target.value)} className="mt-1.5 w-56" />
            </label>
            <label className="text-sm font-medium">
              Destination mailbox
              <Input aria-label="New destination mailbox" value={newDest} onChange={(e) => setNewDest(e.target.value)} className="mt-1.5 w-56" />
            </label>
            <Button type="button" variant="outline" size="sm"
              onClick={() => {
                const s = newSource.trim();
                const d = newDest.trim();
                if (!s || !d) return;
                setPairs((p) => [...p, { sourceMailbox: s, destMailbox: d }]);
                setNewSource("");
                setNewDest("");
              }}>
              Add pair
            </Button>
          </div>
          {csvErrors.length ? <ul className="space-y-0.5 text-sm text-error">{csvErrors.map((e) => <li key={e}>{e}</li>)}</ul> : null}
          {pairs.length ? (
            <div className="overflow-x-auto rounded-[var(--radius)] border border-border">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border bg-surface text-left text-xs font-medium tracking-wide text-fg-muted uppercase">
                    <th className="px-3 py-2">From</th><th className="px-3 py-2">To</th><th className="px-3 py-2">Status</th>
                  </tr>
                </thead>
                <tbody>{pairs.map((p, i) => {
                  const valid = Boolean(p.sourceMailbox && p.destMailbox);
                  return (
                    <tr key={i} className="border-b border-border last:border-0">
                      <td className="mono px-3 py-2">{p.sourceMailbox}</td>
                      <td className="mono px-3 py-2">{p.destMailbox}</td>
                      <td className="px-3 py-2">
                        {valid ? (
                          <span className="inline-flex items-center gap-1.5 text-success"><Check size={14} aria-hidden />valid</span>
                        ) : (
                          <span className="inline-flex items-center gap-1.5 text-warning"><AlertTriangle size={14} aria-hidden />incomplete</span>
                        )}
                      </td>
                    </tr>
                  );
                })}</tbody>
              </table>
            </div>
          ) : null}
        </div>
      ) : (
        <p className="text-sm text-fg-muted">Migrating one mailbox. Confirm and continue.</p>
      )}

      <label className="block text-sm font-medium">
        Only mail since <span className="font-normal text-fg-muted">(optional — limits a migrate or reconcile to a recent window)</span>
        <Input aria-label="Since date" type="date" value={since} onChange={(e) => setSince(e.target.value)} className="mt-1.5 w-48" />
      </label>

      <div>
        <button type="button" className="inline-flex items-center gap-1 text-sm text-fg-muted hover:text-fg" aria-expanded={showAdvanced}
          onClick={() => setShowAdvanced((s) => !s)}>
          <ChevronRight size={14} aria-hidden className={`transition-transform ${showAdvanced ? "rotate-90" : ""}`} />
          Advanced
        </button>
        {showAdvanced ? (
          <div className="mt-3 space-y-3">
            <label className="block text-sm font-medium">Include folders<Input aria-label="Include folders" className="mt-1.5" /></label>
            <label className="block text-sm font-medium">Exclude folders<Input aria-label="Exclude folders" className="mt-1.5" /></label>
          </div>
        ) : null}
      </div>

      <Button type="button" onClick={() => void onContinue()}>Continue</Button>
    </div>
  );
}
