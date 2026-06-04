import { WifiOff } from "lucide-react";
import type { ConnectionState } from "../../api/signalr";

export function ReconnectingIndicator({ state }: { state: ConnectionState }) {
  if (state !== "reconnecting") return null;
  return (
    <span role="status" className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
      <WifiOff size={14} aria-hidden /> Reconnecting…
    </span>
  );
}
