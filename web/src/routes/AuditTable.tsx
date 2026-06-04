import type { AuditEntryDto } from "../api/types";

const STATUS_LABEL: Record<AuditEntryDto["status"], string> = {
  migrated: "✓ migrated", skipped: "⤫ skipped", failed: "✕ failed",
};

export function AuditTable({ entries }: { entries: AuditEntryDto[] }) {
  if (entries.length === 0) {
    return <p className="text-sm text-fg-muted">No audit entries yet.</p>;
  }
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="text-left text-fg-muted"><th>Subject</th><th>Date</th><th>Folder</th><th>Status</th></tr>
      </thead>
      <tbody>
        {entries.map((e, i) => (
          <tr key={i} className="border-t border-border">
            {/* React escapes text children by default — no dangerouslySetInnerHTML anywhere */}
            <td>{e.subject ?? "(hidden)"}</td>
            <td className="mono">{e.messageDate.slice(0, 10)}</td>
            <td className="mono">{e.sourceFolder}</td>
            <td>{STATUS_LABEL[e.status]}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
