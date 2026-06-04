export function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div role="alert" className="rounded-[6px] border border-[color:var(--error-line)] bg-[color:var(--error-bg)] p-4">
      <p className="text-fg">{message}</p>
      <button type="button" onClick={onRetry} className="mt-3 rounded-[8px] border border-border px-3 py-1.5">Retry</button>
    </div>
  );
}
