import { AlertTriangle, ChevronRight } from "lucide-react";
import { useState } from "react";

export interface ErrorAlertProps {
  message: string;
  helpLabel?: string;
  helpHref?: string;
  technicalDetail?: string | null;
  traceId?: string | null;
}

export function ErrorAlert({ message, helpLabel, helpHref, technicalDetail, traceId }: ErrorAlertProps) {
  const [open, setOpen] = useState(false);
  const hasTech = Boolean(technicalDetail || traceId);
  return (
    <div role="alert" className="rounded-[6px] border border-[color:var(--throttled-line)] bg-[color:var(--throttled-bg)] p-3 text-sm">
      <div className="flex items-start gap-2">
        <AlertTriangle size={16} className="mt-0.5 text-throttled" aria-hidden />
        <div className="space-y-1">
          <p className="text-fg">{message}</p>
          {helpHref ? <a href={helpHref} className="text-accent">{helpLabel ?? "Learn more"}</a> : null}
          {hasTech ? (
            <div>
              <button
                type="button"
                onClick={() => setOpen((o) => !o)}
                className="inline-flex items-center gap-1 text-fg-muted hover:text-fg"
                aria-expanded={open}
              >
                <ChevronRight size={13} aria-hidden className={`transition-transform ${open ? "rotate-90" : ""}`} />
                Technical details
              </button>
              {open ? (
                <pre className="mono mt-1 whitespace-pre-wrap rounded bg-surface-2 p-2 text-fg-muted">
                  {technicalDetail}
                  {traceId ? `\ntrace: ${traceId}` : ""}
                </pre>
              ) : null}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
