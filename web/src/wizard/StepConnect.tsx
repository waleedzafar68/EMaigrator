import { useMemo, useState } from "react";
import { useNavigate, useOutletContext, useParams } from "react-router-dom";
import type { AuthMethod, ConnectionSide, ConnectionTestResult, MigrationDto, ProviderId } from "../api/types";
import { putConnection, testConnection } from "../api/migrations";
import { ErrorAlert } from "../components/ErrorAlert";
import { imapDefaults, workmailHost, WORKMAIL_REGIONS, type WorkmailRegion } from "./connectPresets";

function OAuthGuide({ provider }: { provider: ProviderId }) {
  const [skip, setSkip] = useState(false);
  return (
    <div className="space-y-3">
      <button type="button" className="text-sm text-accent" onClick={() => setSkip((s) => !s)}>
        {skip ? "Show me the setup guide" : "I already have an app — just let me paste credentials"}
      </button>
      {!skip ? (
        <ol className="list-decimal space-y-1 pl-5 text-sm text-fg-muted">
          <li>Open the {provider === "graph" ? "Azure portal" : "Google Cloud console"} and create an app registration.</li>
          <li>Grant the least-privilege mail permission and admin consent.</li>
          <li>Copy the values below back into EMaigrator.</li>
        </ol>
      ) : null}
    </div>
  );
}

export function StepConnect() {
  const { side = "from" } = useParams<{ side: ConnectionSide }>();
  const { migration } = useOutletContext<{ migration: MigrationDto }>();
  const navigate = useNavigate();
  const provider = (side === "from" ? migration.from : migration.to) as ProviderId;

  const [region, setRegion] = useState<WorkmailRegion>("us-east-1");
  const [advanced, setAdvanced] = useState(false);
  const [host, setHost] = useState("");
  const [username, setUsername] = useState("");
  const [secret, setSecret] = useState("");
  const [result, setResult] = useState<ConnectionTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const effectiveHost = useMemo(
    () => (advanced ? host : provider === "imap" ? workmailHost(region) : ""),
    [advanced, host, provider, region],
  );

  const auth: AuthMethod = provider === "imap" ? "ImapBasic" : provider === "graph" ? "GraphAppOAuth" : "GmailServiceAccountDwd";

  async function onTest() {
    setTesting(true);
    setResult(null);
    try {
      const settings: Record<string, string> =
        provider === "imap"
          ? { host: effectiveHost, port: String(imapDefaults.port), region, accountEmail: username }
          : { accountEmail: username };
      await putConnection(migration.id, side, { auth, settings, secret });
      setResult(await testConnection(migration.id, side));
    } catch (e) {
      setResult({
        ok: false,
        folderCount: 0,
        messageCount: 0,
        errorCode: null,
        rawDetail: e instanceof Error ? e.message : "Unexpected error",
      });
    } finally {
      setTesting(false);
    }
  }

  function onContinue() {
    navigate(side === "from" ? `/migrations/${migration.id}/connect/to` : `/migrations/${migration.id}/scope`);
  }

  return (
    <div className="space-y-5">
      <h2 className="text-[length:var(--fs-h1)] font-semibold">Connect {side === "from" ? "From" : "To"}</h2>

      {provider === "imap" ? (
        <div className="space-y-3">
          {!advanced ? (
            <label className="block text-sm">
              Region
              <select aria-label="Region" value={region} onChange={(e) => setRegion(e.target.value as WorkmailRegion)}
                className="mt-1 block h-[var(--control-h)] rounded-[6px] border border-border-strong px-2">
                {WORKMAIL_REGIONS.map((r) => <option key={r} value={r}>{r}</option>)}
              </select>
              <a href="/help/workmail-region" className="ml-2 text-accent">How do I find my region?</a>
            </label>
          ) : null}
          <p className="mono text-sm text-fg-muted">Server: {effectiveHost || "—"} Port: {imapDefaults.port} 🔒</p>
          <button type="button" className="text-sm text-accent" onClick={() => setAdvanced((a) => !a)}>
            {advanced ? "Use a provider preset" : "Advanced / custom server"}
          </button>
          {advanced ? (
            <label className="block text-sm">Server host
              <input value={host} onChange={(e) => setHost(e.target.value)}
                className="mt-1 block h-[var(--control-h)] w-full rounded-[6px] border border-border-strong px-2" />
            </label>
          ) : null}
          <label className="block text-sm">Username
            <input aria-label="Username" value={username} onChange={(e) => setUsername(e.target.value)}
              className="mt-1 block h-[var(--control-h)] w-full rounded-[6px] border border-border-strong px-2" />
          </label>
          <label className="block text-sm">Password
            <input aria-label="Password" type="password" value={secret} onChange={(e) => setSecret(e.target.value)}
              className="mt-1 block h-[var(--control-h)] w-full rounded-[6px] border border-border-strong px-2" />
          </label>
        </div>
      ) : (
        <OAuthGuide provider={provider} />
      )}

      <p className="text-sm text-fg-muted">🔒 We read mail to migrate it. We never store contents.</p>

      <button type="button" onClick={() => void onTest()} disabled={testing}
        className="rounded-[8px] border border-border px-4 py-2">
        {testing ? "Testing…" : "Test connection"}
      </button>

      {result?.ok ? (
        <p role="status" className="text-success">
          Connected — found {result.folderCount} folders, {result.messageCount.toLocaleString()} messages.
        </p>
      ) : null}
      {result && !result.ok ? (
        <ErrorAlert
          message="We couldn't connect. WorkMail needs an app password, not your normal password."
          helpLabel="How to create one" helpHref="/help/workmail-app-password"
          technicalDetail={`${result.errorCode ?? ""} ${result.rawDetail ?? ""}`.trim()}
        />
      ) : null}

      <button type="button" disabled={!result?.ok} onClick={onContinue}
        className="block rounded-[8px] bg-accent px-4 py-2 text-accent-fg disabled:opacity-40">
        Continue
      </button>
    </div>
  );
}
