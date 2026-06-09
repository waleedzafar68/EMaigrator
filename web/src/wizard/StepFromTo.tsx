import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import { ArrowRight, Check } from "lucide-react";
import type { ProviderId } from "../api/types";
import { providerName } from "../components/ProviderRoute";
import { Button } from "../components/ui/button";
import { useDraft } from "./useDraft";

const PROVIDERS: { id: ProviderId; name: string }[] = [
  { id: "imap", name: "WorkMail" },
  { id: "graph", name: "Microsoft 365" },
  { id: "gmail", name: "Google" },
];

function Picker({ side, value, onChange }: { side: "From" | "To"; value: ProviderId | null; onChange: (p: ProviderId) => void }) {
  return (
    <fieldset className="space-y-2">
      <legend className="text-sm font-medium text-fg-muted">{side}</legend>
      <div role="radiogroup" className="grid grid-cols-3 gap-2">
        {PROVIDERS.map((p) => {
          const selected = value === p.id;
          return (
            <button
              key={p.id}
              type="button"
              role="radio"
              aria-checked={selected}
              aria-label={`${side} ${p.name}`}
              onClick={() => onChange(p.id)}
              className={`relative rounded-[var(--radius)] border p-3 text-center text-sm transition-colors ${
                selected
                  ? "border-accent bg-accent-subtle text-fg ring-1 ring-accent"
                  : "border-border hover:border-border-strong hover:bg-surface-2"
              }`}
            >
              {selected ? (
                <span className="absolute top-1.5 right-1.5 text-accent">
                  <Check size={14} aria-hidden />
                </span>
              ) : null}
              {p.name}
            </button>
          );
        })}
      </div>
    </fieldset>
  );
}

export function StepFromTo() {
  const { migration } = useOutletContext<{ migration: { id: string } }>();
  const { saveEndpoints } = useDraft(migration.id);
  const navigate = useNavigate();
  const [from, setFrom] = useState<ProviderId | null>(null);
  const [to, setTo] = useState<ProviderId | null>(null);

  async function onContinue() {
    if (!from || !to) return;
    await saveEndpoints(from, to);
    navigate(`/migrations/${migration.id}/connect/from`);
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-[length:var(--fs-h1)] font-semibold">Where are we moving mail?</h2>
        <p className="mt-1 text-sm text-fg-muted">Pick the source you're moving from and the destination you're moving to.</p>
      </div>
      <div className="grid gap-6 md:grid-cols-2">
        <Picker side="From" value={from} onChange={setFrom} />
        <Picker side="To" value={to} onChange={setTo} />
      </div>
      {from && to ? (
        <div className="flex items-center gap-2 rounded-[var(--radius)] border border-accent-line bg-accent-subtle px-4 py-3 text-sm">
          <span className="font-medium">{providerName(from)}</span>
          <ArrowRight size={15} aria-hidden className="text-accent" />
          <span className="font-medium">{providerName(to)}</span>
          <span className="ml-1 text-fg-muted">
            — moving mail from {providerName(from)} to {providerName(to)}.
          </span>
        </div>
      ) : null}
      <Button type="button" disabled={!from || !to} onClick={() => void onContinue()}>
        Continue
      </Button>
    </div>
  );
}
