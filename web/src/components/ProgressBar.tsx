export function ProgressBar({ value, label }: { value: number; label?: string }) {
  return (
    <div role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={100} aria-label={label ?? "Progress"}
      className="h-2 w-full overflow-hidden rounded-full bg-surface-2">
      <div className="h-full bg-accent transition-[width] duration-200" style={{ width: `${value}%` }} />
    </div>
  );
}
