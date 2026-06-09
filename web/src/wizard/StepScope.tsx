import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { AlertTriangle, Check, ChevronRight, Loader2, ShieldCheck, User, Users } from "lucide-react";
import type { MailboxPairDto, MigrationDto, MigrationMode, ScopeRequest } from "../api/types";
import { reconcile } from "../api/migrations";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { ErrorAlert } from "../components/ErrorAlert";
import { errorAlertProps } from "../components/states/fromApiError";
import { useDraft } from "./useDraft";
import { parsePairsCsv } from "./csv";

interface ScopeCtx { migration: MigrationDto; canBatch: boolean; mode?: MigrationMode; }

export function StepScope() {
  // `mode` defaults to migrate so a context that doesn't supply it stays on the full-migration path.
  const { migration, canBatch, mode = "migrate" } = useOutletContext<ScopeCtx>();
  const { saveScope } = useDraft(migration.id);
  const navigate = useNavigate();
  const isReconcile = mode === "reconcile";
  const [isBatch, setIsBatch] = useState(false);
  const [pairs, setPairs] = useState<MailboxPairDto[]>([]);
  const [csvErrors, setCsvErrors] = useState<string[]>([]);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [newSource, setNewSource] = useState("");
  const [newDest, setNewDest] = useState("");
  const [since, setSince] = useState("");
  const [before, setBefore] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);

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

  // A date-only input is widened to an explicit UTC instant so the API's DateTimeOffset binder accepts it.
  const toInstant = (d: string) => new Date(`${d}T00:00:00Z`).toISOString();

  async function onContinue() {
    setSaving(true);
    setError(null);
    try {
      const scope: ScopeRequest = { isBatch, pairs };
      if (since) scope.since = toInstant(since);
      if (before) scope.before = toInstant(before);
      await saveScope(scope);
      if (isReconcile) {
        // Reconcile has no approval gate — persist scope, kick the reconcile, and watch it run live.
        await reconcile(migration.id);
        navigate(`/migrations/${migration.id}/run`);
      } else {
        navigate(`/migrations/${migration.id}/review`);
      }
    } catch (e) {
      setError(e); // surface the reason — never a silent dead Continue
    } finally {
      setSaving(false);
    }
  }

  const continueDisabled = saving || (isBatch && pairs.length === 0);

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-[length:var(--fs-h1)] font-semibold">
          {isReconcile ? "What should we reconcile?" : "What should we migrate?"}
        </h2>
        <p className="mt-1 text-sm text-fg-muted">Choose how many mailboxes this run covers.</p>
      </div>

      {isReconcile ? (
        <div className="flex items-start gap-2 rounded-[var(--radius)] border border-accent-line bg-accent-subtle px-4 py-3 text-sm">
          <ShieldCheck size={16} aria-hidden className="mt-0.5 text-accent" />
          <p className="text-fg-muted">
            Reconcile compares the source against the <span className="font-medium text-fg">live destination</span> and
            copies only what's missing (and backfills missing attachments). It is non-destructive and never duplicates —
            safe to re-run.
          </p>
        </div>
      ) : null}

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

      {isReconcile ? (
        <>
          <label className="block text-sm font-medium">
            And before <span className="font-normal text-fg-muted">(optional — upper bound of the reconcile window)</span>
            <Input aria-label="Before date" type="date" value={before} onChange={(e) => setBefore(e.target.value)} className="mt-1.5 w-48" />
          </label>

          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Match by</legend>
            <div role="radiogroup" aria-label="Match by" className="grid grid-cols-2 gap-2 sm:max-w-md">
              <button
                type="button"
                role="radio"
                aria-checked
                className="rounded-[var(--radius)] border border-accent bg-accent-subtle p-3 text-left text-sm ring-1 ring-accent"
              >
                <span className="block font-medium">Metadata</span>
                <span className="block text-xs text-fg-muted">Message-ID + attachment list</span>
              </button>
              <button
                type="button"
                role="radio"
                aria-checked={false}
                disabled
                title="Coming soon"
                className="rounded-[var(--radius)] border border-border p-3 text-left text-sm opacity-40"
              >
                <span className="block font-medium">Content hash</span>
                <span className="block text-xs text-fg-muted">Coming soon</span>
              </button>
            </div>
          </fieldset>
        </>
      ) : (
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
      )}

      {error ? <ErrorAlert {...errorAlertProps(error)} /> : null}

      <div className="space-y-1.5">
        <Button type="button" disabled={continueDisabled} onClick={() => void onContinue()}>
          {saving ? (
            <span className="inline-flex items-center gap-1.5">
              <Loader2 size={15} aria-hidden className="animate-spin" />
              {isReconcile ? "Starting…" : "Saving…"}
            </span>
          ) : isReconcile ? (
            "Start reconcile / repair"
          ) : (
            "Continue"
          )}
        </Button>
        {isBatch && pairs.length === 0 ? (
          <p className="text-xs text-fg-muted">Add at least one valid mailbox pair to continue.</p>
        ) : null}
      </div>
    </div>
  );
}
