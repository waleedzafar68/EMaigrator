export function EmptyState({ title, description, actionLabel, onAction }: {
  title: string; description?: string; actionLabel: string; onAction: () => void;
}) {
  return (
    <div className="mx-auto max-w-[480px] py-16 text-center">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">{title}</h2>
      {description ? <p className="mt-2 text-fg-muted">{description}</p> : null}
      <button type="button" onClick={onAction} className="mt-6 rounded-[8px] bg-accent px-5 py-3 text-accent-fg">
        {actionLabel}
      </button>
    </div>
  );
}
