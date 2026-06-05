import { useEffect, useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import type { MigrationDto, PreflightPlanDto, RemediationAction } from "../api/types";
import { approve, getPreflight, startPreflight } from "../api/migrations";
import { ErrorAlert } from "../components/ErrorAlert";
import { errorAlertProps } from "../components/states/fromApiError";
import { formatBytes, formatDuration } from "./format";

const ACTION_LABEL: Record<RemediationAction, string> = {
  None: "Keep as-is", RetryWithBackoff: "Retry", FlattenFolder: "Flatten",
  SanitizeFolderName: "Sanitize", RenameFolder: "Rename", MergeFolder: "Merge", SkipMessage: "Skip & log",
};

export function StepReview() {
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const navigate = useNavigate();
  const [plan, setPlan] = useState<PreflightPlanDto | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [resolutions, setResolutions] = useState<Record<string, RemediationAction>>({});

  useEffect(() => {
    let active = true;
    let timerId: ReturnType<typeof setTimeout> | undefined;
    const poll = async () => {
      try {
        const p = await getPreflight(migration.id);
        if (!active) return;
        setPlan(p);
        if (p.scanning) timerId = setTimeout(() => void poll(), 1500);
        else setResolutions(Object.fromEntries(p.issues.map((i) => [i.issueType, i.recommendedAction])));
      } catch (e) {
        if (active) setError(e); // 401 redirects globally; surface anything else instead of hanging
      }
    };
    void startPreflight(migration.id).catch(() => {}).finally(() => { if (active) void poll(); });
    return () => { active = false; clearTimeout(timerId); };
  }, [migration.id]);

  if (error) return <ErrorAlert {...errorAlertProps(error)} />;
  if (!plan || plan.scanning) {
    return <div role="status" aria-label="Reviewing your mailboxes">Reviewing your mailboxes…</div>;
  }

  const overQuota = plan.usage ? plan.usage.used + plan.estimate.mailboxCount > plan.usage.quota : false;
  const overCap = plan.usage ? plan.usage.overCapMailboxes > 0 : false;
  const blocked = overQuota || overCap || plan.issues.some((i) => i.severity === "Blocker");
  const e = plan.estimate;

  async function onApprove() {
    await approve(migration.id, { resolutions });
    navigate(`/migrations/${migration.id}/run`);
  }

  if (plan.issues.length === 0 && !plan.usage) {
    return (
      <div className="space-y-3 rounded-[6px] border border-border p-[var(--card-pad)]">
        <h2 className="flex items-center gap-2 text-[length:var(--fs-h1)] font-semibold">✓ Ready to migrate</h2>
        <p className="mono text-sm">{e.mailboxCount} mailbox · {e.folderCount} folders</p>
        <p className="mono text-sm">{e.messageCount.toLocaleString()} messages · {formatBytes(e.totalBytes)}</p>
        <p className="mono text-sm">Estimated: {formatDuration(e.estimatedDurationSeconds)}</p>
        <button type="button" onClick={() => void onApprove()} className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg">Start migration</button>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">
        {plan.issues.length === 0
          ? "Review your plan"
          : `${plan.issues.length} thing${plan.issues.length === 1 ? "" : "s"} to resolve before we start`}
      </h2>
      <ul className="space-y-3">
        {plan.issues.map((i) => (
          <li key={i.issueType} className={`rounded-[6px] border p-3 ${i.severity === "Blocker" ? "border-error" : "border-border"}`}>
            <p>{i.description} {i.severity === "Blocker" ? <span className="text-error">(must fix)</span> : null}</p>
            <label className="mt-2 block text-sm">Resolution
              <select aria-label={`Resolution for ${i.issueType}`} value={resolutions[i.issueType] ?? i.recommendedAction}
                onChange={(ev) => setResolutions((r) => ({ ...r, [i.issueType]: ev.target.value as RemediationAction }))}
                className="ml-2 h-[var(--control-h)] rounded-[6px] border border-border-strong px-2">
                {i.options.map((o) => <option key={o} value={o}>{ACTION_LABEL[o]}</option>)}
              </select>
            </label>
          </li>
        ))}
      </ul>
      <p className="mono text-sm">Summary: {e.mailboxCount} mailboxes · {e.messageCount.toLocaleString()} msgs · {formatDuration(e.estimatedDurationSeconds)}</p>
      {plan.usage ? (
        <p className={overQuota || overCap ? "text-error" : "text-fg-muted"}>
          Needs {e.mailboxCount} mailboxes (you have {plan.usage.quota - plan.usage.used} left)
          {overCap ? ` · ${plan.usage.overCapMailboxes} mailboxes exceed the ${plan.usage.capGb} GB cap → upgrade to proceed` : ""}
        </p>
      ) : null}
      <button type="button" disabled={blocked} onClick={() => void onApprove()}
        className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg disabled:opacity-40">
        Approve plan &amp; start
      </button>
    </div>
  );
}
