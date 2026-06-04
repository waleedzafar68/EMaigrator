export function Skeleton({ label = "Loading", className = "h-24 w-full" }: { label?: string; className?: string }) {
  return (
    <div role="status" aria-busy="true" aria-label={label}
      className={`animate-[em-skeleton_1.2s_ease-in-out_infinite] rounded bg-surface-2 ${className}`} />
  );
}
