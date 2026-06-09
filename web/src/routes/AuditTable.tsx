import { Check, FileX2, X } from "lucide-react";
import type { JSX } from "react";
import type { AuditEntryDto } from "../api/types";

type AuditStatus = AuditEntryDto["status"];

const STATUS: Record<AuditStatus, { label: string; icon: JSX.Element; cls: string }> = {
  migrated: { label: "migrated", icon: <Check size={14} aria-hidden />, cls: "text-success" },
  skipped: { label: "skipped", icon: <FileX2 size={14} aria-hidden />, cls: "text-fg-muted" },
  failed: { label: "failed", icon: <X size={14} aria-hidden />, cls: "text-error" },
};

function StatusCell({ status }: { status: AuditStatus }) {
  const { label, icon, cls } = STATUS[status];
  return (
    <span className={`inline-flex items-center gap-1.5 ${cls}`}>
      {icon}
      {label}
    </span>
  );
}

export function AuditTable({ entries }: { entries: AuditEntryDto[] }) {
  if (entries.length === 0) {
    return (
      <div className="rounded-[var(--radius)] border border-dashed border-border py-10 text-center">
        <p className="text-sm text-fg-muted">No audit entries yet.</p>
        <p className="mt-1 text-xs text-fg-subtle">Migrated, skipped, and failed messages will appear here.</p>
      </div>
    );
  }
  return (
    <div className="overflow-x-auto rounded-[var(--radius)] border border-border">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-border bg-surface text-left text-xs font-medium tracking-wide text-fg-muted uppercase">
            <th className="px-3 py-2">Subject</th>
            <th className="px-3 py-2">Date</th>
            <th className="px-3 py-2">Folder</th>
            <th className="px-3 py-2">Status</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e, i) => (
            <tr key={i} className="border-b border-border last:border-0 hover:bg-surface-2/60">
              {/* React escapes text children by default — raw HTML is never injected */}
              <td className="max-w-[28ch] truncate px-3 py-2" title={e.subject ?? undefined}>
                {e.subject ?? <span className="text-fg-subtle">(hidden)</span>}
              </td>
              <td className="mono px-3 py-2 text-fg-muted">{e.date.slice(0, 10)}</td>
              <td className="mono px-3 py-2 text-fg-muted">{e.sourceFolder}</td>
              <td className="px-3 py-2">
                <StatusCell status={e.status} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
