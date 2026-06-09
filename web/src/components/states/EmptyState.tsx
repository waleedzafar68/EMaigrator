import { Inbox } from "lucide-react";
import { buttonVariants } from "../ui/button";

export function EmptyState({ title, description, actionLabel, onAction }: {
  title: string; description?: string; actionLabel: string; onAction: () => void;
}) {
  return (
    <div className="mx-auto max-w-[480px] py-16 text-center">
      <div className="mx-auto mb-5 flex h-12 w-12 items-center justify-center rounded-2xl bg-accent-subtle text-accent">
        <Inbox size={22} aria-hidden />
      </div>
      <h2 className="text-[length:var(--fs-h1)] font-semibold">{title}</h2>
      {description ? <p className="mx-auto mt-2 max-w-[40ch] text-fg-muted">{description}</p> : null}
      <button type="button" onClick={onAction} className={`mt-6 ${buttonVariants({ size: "lg" })}`}>
        {actionLabel}
      </button>
    </div>
  );
}
