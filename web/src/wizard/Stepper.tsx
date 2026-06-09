import { Link } from "react-router-dom";
import { Check } from "lucide-react";
import { STEPS } from "./steps";

export function canAdvanceTo(target: number, maxReached: number): boolean {
  return target <= maxReached + 1;
}

export function Stepper({ current, maxReached, migrationId }: { current: number; maxReached: number; migrationId: string }) {
  return (
    <ol className="mb-10 flex" aria-label="Migration steps">
      {STEPS.map((s, i) => {
        const done = i < current;
        const isCurrent = i === current;
        const reachable = i <= maxReached;

        const circle = (
          <span
            aria-hidden
            className={[
              "flex h-7 w-7 shrink-0 items-center justify-center rounded-full border text-xs font-medium transition-colors",
              done
                ? "border-accent bg-accent text-accent-fg"
                : isCurrent
                  ? "border-2 border-accent bg-accent-subtle text-accent"
                  : reachable
                    ? "border-border bg-surface text-fg-muted"
                    : "border-border bg-surface text-fg-subtle",
            ].join(" ")}
          >
            {done ? <Check size={14} /> : i + 1}
          </span>
        );

        const label = (
          <span
            className={[
              "text-center text-[11px] leading-tight transition-colors",
              isCurrent ? "font-medium text-fg" : done || reachable ? "text-fg-muted" : "text-fg-subtle",
            ].join(" ")}
          >
            {s.label}
          </span>
        );

        const inner = (
          <>
            {circle}
            {label}
          </>
        );

        return (
          <li key={s.key} className="relative flex flex-1 flex-col items-center">
            {i > 0 ? (
              <span
                aria-hidden
                className={`absolute top-[13px] right-[50%] left-[-50%] h-0.5 ${i <= current ? "bg-accent" : "bg-border"}`}
              />
            ) : null}
            {reachable && !isCurrent ? (
              <Link
                to={`/migrations/${migrationId}/${s.path}`}
                className="relative z-10 flex flex-col items-center gap-2 rounded-md px-1 hover:opacity-80"
              >
                {inner}
              </Link>
            ) : (
              <div
                aria-current={isCurrent ? "step" : undefined}
                aria-disabled={!reachable ? "true" : undefined}
                className="relative z-10 flex flex-col items-center gap-2 px-1"
              >
                {inner}
              </div>
            )}
          </li>
        );
      })}
    </ol>
  );
}
