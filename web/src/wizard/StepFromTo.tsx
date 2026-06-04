import { useState } from "react";
import { useNavigate, useOutletContext } from "react-router-dom";
import type { ProviderId } from "../api/types";
import { providerName } from "../components/ProviderRoute";
import { useDraft } from "./useDraft";

const PROVIDERS: { id: ProviderId; name: string }[] = [
  { id: "imap", name: "WorkMail" },
  { id: "graph", name: "Microsoft 365" },
  { id: "gmail", name: "Google" },
];

function Picker({ side, value, onChange }: { side: "From" | "To"; value: ProviderId | null; onChange: (p: ProviderId) => void }) {
  return (
    <fieldset className="space-y-2">
      <legend className="text-sm font-medium">{side}</legend>
      <div role="radiogroup" className="grid grid-cols-3 gap-2">
        {PROVIDERS.map((p) => (
          <button key={p.id} type="button" role="radio" aria-checked={value === p.id}
            aria-label={`${side} ${p.name}`} onClick={() => onChange(p.id)}
            className={`rounded-[6px] border p-3 text-sm ${value === p.id ? "border-accent ring-1 ring-accent" : "border-border"}`}>
            {p.name}
          </button>
        ))}
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
      <h2 className="text-[length:var(--fs-h1)] font-semibold">Where are we moving mail?</h2>
      <div className="grid gap-6 md:grid-cols-2">
        <Picker side="From" value={from} onChange={setFrom} />
        <Picker side="To" value={to} onChange={setTo} />
      </div>
      {from && to ? (
        <p className="text-fg-muted">
          You&apos;re moving mail from {providerName(from)} to {providerName(to)}.
        </p>
      ) : null}
      <button type="button" disabled={!from || !to} onClick={() => void onContinue()}
        className="rounded-[8px] bg-accent px-4 py-2 text-accent-fg disabled:opacity-40">
        Continue
      </button>
    </div>
  );
}
