import { AlertTriangle, Check, Circle, Play, RotateCcw, X } from "lucide-react";
import type { JSX } from "react";

export type ChipStatus = "done" | "running" | "throttled" | "warning" | "error" | "queued";

const MAP: Record<ChipStatus, { label: string; icon: JSX.Element; cls: string }> = {
  done: { label: "Migrated", icon: <Check size={14} aria-hidden />, cls: "text-success" },
  running: { label: "Running", icon: <Play size={14} aria-hidden />, cls: "text-accent" },
  throttled: { label: "Slowing to respect limits", icon: <RotateCcw size={14} aria-hidden />, cls: "text-throttled" },
  warning: { label: "Needs decision", icon: <AlertTriangle size={14} aria-hidden />, cls: "text-warning" },
  error: { label: "Failed", icon: <X size={14} aria-hidden />, cls: "text-error" },
  queued: { label: "Queued", icon: <Circle size={14} aria-hidden />, cls: "text-idle" },
};

export function StatusChip({ status }: { status: ChipStatus }) {
  const { label, icon, cls } = MAP[status];
  return (
    <span role="status" aria-label={label}
      className={`inline-flex items-center gap-1.5 rounded-[4px] px-2 py-0.5 text-sm ${cls}`}>
      {icon}
      <span>{label}</span>
    </span>
  );
}

export function jobStatusToChip(status: string): ChipStatus {
  switch (status) {
    case "Completed": return "done";
    case "Running": case "PreFlight": return "running";
    case "Paused": case "Queued": case "AwaitingApproval": case "Draft": return "queued";
    case "Partial": return "warning";
    case "Failed": return "error";
    case "Cancelled": return "error";
    default: return "queued";
  }
}
