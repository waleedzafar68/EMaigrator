import { useEffect, useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { CheckCircle2, Clock, FolderTree, HardDrive, Loader2, Mail } from "lucide-react";
import type { MigrationDto, PreflightPlanDto, RemediationAction } from "../api/types";
import { approve, getPreflight, startPreflight } from "../api/migrations";
import { Button } from "../components/ui/button";
import { ErrorAlert } from "../components/ErrorAlert";
import { errorAlertProps } from "../components/states/fromApiError";
import { formatBytes, formatDuration } from "./format";

const ACTION_LABEL: Record<RemediationAction, string> = {
  None: "Keep as-is", RetryWithBackoff: "Retry", FlattenFolder: "Flatten",
  SanitizeFolderName: "Sanitize", RenameFolder: "Rename", MergeFolder: "Merge", SkipMessage: "Skip & log",
};

const resolutionSelectClass =
  "ml-2 h-8 rounded-md border border-input bg-transparent px-2 text-sm shadow-xs outline-none transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 dark:bg-input/30";

// A labelled metric chip used in the Ready / summary card.
function Metric({ icon, children }: { icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
      <span className="text-fg-subtle">{icon}</span>
      <span className="mono text-fg">{children}</span>
    </span>
  );
}

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
    return (
      <div role="status" aria-label="Reviewing your mailboxes" className="flex items-center gap-2.5 text-fg-muted">
        <Loader2 size={16} aria-hidden className="animate-spin text-accent" />
        Reviewing your mailboxes…
      </div>
    );
  }

  const blocked = plan.issues.some((i) => i.severity === "Blocker");
  const e = plan.estimate;

  async function onApprove() {
    await approve(migration.id, { resolutions });
    navigate(`/migrations/${migration.id}/run`);
  }

  if (plan.issues.length === 0) {
    return (
      <div className="space-y-4 rounded-[var(--radius)] border border-success-line bg-success-bg p-[var(--card-pad)]">
        <h2 className="flex items-center gap-2 text-[length:var(--fs-h1)] font-semibold">
          <CheckCircle2 size={22} aria-hidden className="text-success" /> Ready to migrate
        </h2>
        <div className="flex flex-wrap gap-x-5 gap-y-2">
          <Metric icon={<Mail size={14} aria-hidden />}>{e.mailboxCount} mailbox · {e.folderCount} folders</Metric>
          <Metric icon={<HardDrive size={14} aria-hidden />}>{e.messageCount.toLocaleString()} messages · {formatBytes(e.totalBytes)}</Metric>
          <Metric icon={<Clock size={14} aria-hidden />}>Estimated {formatDuration(e.estimatedDurationSeconds)}</Metric>
        </div>
        <Button type="button" onClick={() => void onApprove()}>Start migration</Button>
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
          <li key={i.issueType} className={`rounded-[var(--radius)] border p-3 ${i.severity === "Blocker" ? "border-error bg-error-bg" : "border-warning-line bg-warning-bg"}`}>
            <p className="flex items-start gap-2">
              <FolderTree size={16} aria-hidden className={`mt-0.5 shrink-0 ${i.severity === "Blocker" ? "text-error" : "text-warning"}`} />
              <span>{i.description} {i.severity === "Blocker" ? <span className="font-medium text-error">(must fix)</span> : null}</span>
            </p>
            <label className="mt-2 block text-sm text-fg-muted">Resolution
              <select aria-label={`Resolution for ${i.issueType}`} value={resolutions[i.issueType] ?? i.recommendedAction}
                onChange={(ev) => setResolutions((r) => ({ ...r, [i.issueType]: ev.target.value as RemediationAction }))}
                className={resolutionSelectClass}>
                {i.options.map((o) => <option key={o} value={o}>{ACTION_LABEL[o]}</option>)}
              </select>
            </label>
          </li>
        ))}
      </ul>
      <p className="mono inline-flex flex-wrap items-center gap-x-2 text-sm text-fg-muted">
        Summary: {e.mailboxCount} mailboxes · {e.messageCount.toLocaleString()} msgs · {formatDuration(e.estimatedDurationSeconds)}
      </p>
      <Button type="button" disabled={blocked} onClick={() => void onApprove()}>
        Approve plan &amp; start
      </Button>
    </div>
  );
}
