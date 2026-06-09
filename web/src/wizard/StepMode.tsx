import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { Check, Mail, Wrench } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { setMode } from "../api/migrations";
import type { MigrationDto, MigrationMode } from "../api/types";
import { Button } from "../components/ui/button";

const MODES: { id: MigrationMode; title: string; blurb: string; Icon: LucideIcon }[] = [
  {
    id: "migrate",
    title: "Migrate — full copy",
    blurb: "Copy every message from the source into the destination. Best for a fresh move.",
    Icon: Mail,
  },
  {
    id: "reconcile",
    title: "Reconcile / Backfill — repair",
    blurb: "Compare against an existing destination and copy only what's missing. Never duplicates — safe to re-run.",
    Icon: Wrench,
  },
];

export function StepMode() {
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const navigate = useNavigate();
  const [selected, setSelected] = useState<MigrationMode | null>(null);
  const [saving, setSaving] = useState(false);

  async function onContinue() {
    if (!selected) return;
    setSaving(true);
    try {
      await setMode(migration.id, selected);
      navigate(`/migrations/${migration.id}/from-to`);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-[length:var(--fs-h1)] font-semibold">What kind of run is this?</h2>
        <p className="mt-1 text-sm text-fg-muted">
          Pick a full migration into a fresh destination, or reconcile against a destination that already has mail.
        </p>
      </div>

      <div role="radiogroup" aria-label="Migration mode" className="grid gap-3 sm:grid-cols-2">
        {MODES.map((m) => {
          const isSelected = selected === m.id;
          const Icon = m.Icon;
          return (
            <button
              key={m.id}
              type="button"
              role="radio"
              aria-checked={isSelected}
              aria-label={m.title}
              onClick={() => setSelected(m.id)}
              className={`relative flex flex-col gap-2 rounded-[var(--radius)] border p-4 text-left transition-colors ${
                isSelected
                  ? "border-accent bg-accent-subtle ring-1 ring-accent"
                  : "border-border hover:border-border-strong hover:bg-surface-2"
              }`}
            >
              {isSelected ? (
                <span className="absolute top-2 right-2 text-accent">
                  <Check size={16} aria-hidden />
                </span>
              ) : null}
              <Icon size={20} aria-hidden className={isSelected ? "text-accent" : "text-fg-muted"} />
              <span className="text-sm font-medium text-fg">{m.title}</span>
              <span className="text-xs text-fg-muted">{m.blurb}</span>
            </button>
          );
        })}
      </div>

      <Button type="button" disabled={!selected || saving} onClick={() => void onContinue()}>
        {saving ? "Saving…" : "Continue"}
      </Button>
    </div>
  );
}
