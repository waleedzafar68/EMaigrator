import { useMemo, useState } from "react";
import { useNavigate, useOutletContext, useParams } from "react-router-dom";
import type { AuthMethod, ConnectionSide, ConnectionTestResult, MigrationDto, ProviderId } from "../api/types";
import { putConnection, testConnection } from "../api/migrations";
import { ErrorAlert } from "../components/ErrorAlert";
import { imapDefaults, workmailHost, WORKMAIL_REGIONS, type WorkmailRegion } from "./connectPresets";

const inputClass =
  "mt-1 block h-[var(--control-h)] w-full rounded-[6px] border border-border-strong px-2";

function OAuthGuide({ provider }: { provider: ProviderId }) {
  const portal = provider === "graph" ? "Azure portal" : "Google Cloud console";
  return (
    <ol className="list-decimal space-y-1 pl-5 text-sm text-fg-muted">
      {provider === "graph" ? (
        <>
          <li>In the {portal}, register an app and add the <span className="mono">Mail.ReadWrite</span> application permission, then grant admin consent.</li>
          <li>Create a client secret and copy the Directory (tenant) ID, Application (client) ID, and secret value.</li>
          <li>Paste them below — <span className="mono">Account email</span> is the target Exchange mailbox.</li>
        </>
      ) : (
        <>
          <li>In the {portal}, create a service account and enable domain-wide delegation.</li>
          <li>In the Workspace Admin console, authorize the SA client ID for scope <span className="mono">https://mail.google.com/</span> only.</li>
          <li>Paste the full service-account JSON key below — <span className="mono">Account email</span> is the mailbox to read.</li>
        </>
      )}
    </ol>
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
  const [tenantId, setTenantId] = useState("");
  const [clientId, setClientId] = useState("");
  const [secret, setSecret] = useState("");
  const [result, setResult] = useState<ConnectionTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const effectiveHost = useMemo(
    () => (advanced ? host : provider === "imap" ? workmailHost(region) : ""),
    [advanced, host, provider, region],
  );

  const auth: AuthMethod = provider === "imap" ? "ImapBasic" : provider === "graph" ? "GraphAppOAuth" : "GmailServiceAccountDwd";

  function settingsFor(): Record<string, string> {
    if (provider === "imap") {
      return { host: effectiveHost, port: String(imapDefaults.port), region, accountEmail: username };
    }
    if (provider === "graph") {
      return { tenantId, clientId, accountEmail: username };
    }
    return { accountEmail: username };
  }

  async function onTest() {
    setTesting(true);
    setResult(null);
    try {
      // The API shapes `secret` into the connector's bundle key per auth method, so we send the raw
      // credential: the IMAP password, the Graph client-secret value, or the full Gmail SA-JSON contents.
      await putConnection(migration.id, side, { auth, settings: settingsFor(), secret });
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

  const failure =
    provider === "graph"
      ? {
          message: "We couldn't connect. Check the tenant/client IDs and client secret, and that admin consent was granted for Mail.ReadWrite.",
          helpLabel: "Graph app setup",
          helpHref: "/help/graph-app",
        }
      : provider === "gmail"
        ? {
            message: "We couldn't connect. Check the service-account JSON and that domain-wide delegation is authorized for https://mail.google.com/.",
            helpLabel: "Gmail delegation setup",
            helpHref: "/help/gmail-delegation",
          }
        : {
            message: "We couldn't connect. WorkMail needs an app password, not your normal password.",
            helpLabel: "How to create one",
            helpHref: "/help/workmail-app-password",
          };

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
              <input value={host} onChange={(e) => setHost(e.target.value)} className={inputClass} />
            </label>
          ) : null}
          <label className="block text-sm">Username
            <input aria-label="Username" value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} />
          </label>
          <label className="block text-sm">Password
            <input aria-label="Password" type="password" value={secret} onChange={(e) => setSecret(e.target.value)} className={inputClass} />
          </label>
        </div>
      ) : provider === "graph" ? (
        <div className="space-y-3">
          <OAuthGuide provider="graph" />
          <label className="block text-sm">Directory (tenant) ID
            <input aria-label="Tenant ID" value={tenantId} onChange={(e) => setTenantId(e.target.value)} className={inputClass} />
          </label>
          <label className="block text-sm">Application (client) ID
            <input aria-label="Client ID" value={clientId} onChange={(e) => setClientId(e.target.value)} className={inputClass} />
          </label>
          <label className="block text-sm">Account email (target mailbox)
            <input aria-label="Account email" value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} />
          </label>
          <label className="block text-sm">Client secret
            <input aria-label="Client secret" type="password" value={secret} onChange={(e) => setSecret(e.target.value)} className={inputClass} />
          </label>
        </div>
      ) : (
        <div className="space-y-3">
          <OAuthGuide provider="gmail" />
          <label className="block text-sm">Account email (mailbox to read)
            <input aria-label="Account email" value={username} onChange={(e) => setUsername(e.target.value)} className={inputClass} />
          </label>
          <label className="block text-sm">Service account JSON
            <textarea aria-label="Service account JSON" value={secret} onChange={(e) => setSecret(e.target.value)} rows={6}
              className="mono mt-1 block w-full rounded-[6px] border border-border-strong p-2 text-xs" />
          </label>
        </div>
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
          message={failure.message}
          helpLabel={failure.helpLabel} helpHref={failure.helpHref}
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
