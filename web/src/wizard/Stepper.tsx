import { Link } from "react-router-dom";
import { STEPS } from "./steps";

export function canAdvanceTo(target: number, maxReached: number): boolean {
  return target <= maxReached + 1;
}

export function Stepper({ current, maxReached, migrationId }: { current: number; maxReached: number; migrationId: string }) {
  return (
    <ol className="mb-8 flex items-center gap-2" aria-label="Migration steps">
      {STEPS.map((s, i) => {
        const reachable = i <= maxReached;
        const isCurrent = i === current;
        const content = (
          <span className="flex items-center gap-2 text-sm">
            <span className={`flex h-6 w-6 items-center justify-center rounded-full text-xs ${isCurrent ? "bg-accent text-accent-fg" : reachable ? "bg-surface-2 text-fg" : "bg-surface-2 text-fg-subtle"}`}>{i + 1}</span>
            {s.label}
          </span>
        );
        return (
          <li key={s.key}>
            {reachable && !isCurrent ? (
              <Link to={`/migrations/${migrationId}/${s.path}`}>{content}</Link>
            ) : (
              <div aria-current={isCurrent ? "step" : undefined} aria-disabled={!reachable ? "true" : undefined}>
                {content}
              </div>
            )}
          </li>
        );
      })}
    </ol>
  );
}
