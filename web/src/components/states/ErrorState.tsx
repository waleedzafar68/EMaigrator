import { AlertCircle, RefreshCw } from "lucide-react";
import { Button } from "../ui/button";

export function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div role="alert" className="rounded-[var(--radius)] border border-error-line bg-error-bg p-4">
      <p className="flex items-start gap-2 text-fg">
        <AlertCircle size={16} aria-hidden className="mt-0.5 shrink-0 text-error" />
        {message}
      </p>
      <Button type="button" variant="outline" size="sm" onClick={onRetry} className="mt-3">
        <RefreshCw size={14} aria-hidden /> Retry
      </Button>
    </div>
  );
}
