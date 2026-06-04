import type { ProviderId } from "../api/types";

const NAME: Record<ProviderId, string> = { imap: "WorkMail", graph: "Microsoft 365", gmail: "Google" };

export function providerName(p: ProviderId | null): string {
  return p ? NAME[p] : "—";
}

export function ProviderRoute({ from, to }: { from: ProviderId | null; to: ProviderId | null }) {
  return (
    <span className="inline-flex items-center gap-2 font-medium">
      <span>{providerName(from)}</span>
      <span aria-hidden>→</span>
      <span>{providerName(to)}</span>
    </span>
  );
}
