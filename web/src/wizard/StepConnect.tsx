import { useMemo, useState } from "react";
import { useNavigate, useOutletContext, useParams } from "react-router-dom";
import { CheckCircle2, Eye, EyeOff, Lock, ShieldCheck } from "lucide-react";
import type { AuthMethod, ConnectionSide, ConnectionTestResult, MigrationDto, ProviderId } from "../api/types";
import { putConnection, testConnection } from "../api/migrations";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import { ErrorAlert } from "../components/ErrorAlert";
import { imapDefaults, workmailHost, WORKMAIL_REGIONS, type WorkmailRegion } from "./connectPresets";

const selectClass =
  "mt-1.5 block h-9 rounded-md border border-input bg-transparent px-3 text-sm shadow-xs outline-none transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 dark:bg-input/30";

const Req = () => <span className="text-error" aria-hidden> *</span>;

/** Password/secret input with a reveal toggle. The toggle's label deliberately omits the word
 *  "password" so getByLabelText(/password/i) still resolves to exactly the input. */
function SecretInput({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  const [visible, setVisible] = useState(false);
  return (
    <div className="relative mt-1.5">
      <Input
        aria-label={label}
        type={visible ? "text" : "password"}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="pr-10"
      />
      <button
        type="button"
        aria-label={visible ? "Hide value" : "Show value"}
        onClick={() => setVisible((v) => !v)}
        className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-fg-muted hover:text-fg"
      >
        {visible ? <EyeOff size={16} aria-hidden /> : <Eye size={16} aria-hidden />}
      </button>
    </div>
  );
}

function OAuthGuide({ provider }: { provider: ProviderId }) {
  const portal = provider === "graph" ? "Azure portal" : "Google Cloud console";
  return (
    <ol className="list-decimal space-y-1 rounded-[var(--radius)] border border-border bg-surface p-4 pl-8 text-sm text-fg-muted">
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
        <div className="space-y-4">
          {!advanced ? (
            <label className="block text-sm font-medium">
              Region<Req />
              <span className="flex items-center gap-3">
                <select aria-label="Region" value={region} onChange={(e) => setRegion(e.target.value as WorkmailRegion)} className={selectClass}>
                  {WORKMAIL_REGIONS.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
                <a href="/help/workmail-region" className="mt-1.5 text-sm text-accent hover:underline">How do I find my region?</a>
              </span>
            </label>
          ) : null}
          <p className="mono inline-flex items-center gap-1.5 rounded-[var(--radius)] bg-surface px-3 py-2 text-sm text-fg-muted">
            <Lock size={13} aria-hidden /> Server: {effectiveHost || "—"} · Port: {imapDefaults.port}
          </p>
          <button type="button" className="text-sm text-accent hover:underline" onClick={() => setAdvanced((a) => !a)}>
            {advanced ? "Use a provider preset" : "Advanced / custom server"}
          </button>
          {advanced ? (
            <label className="block text-sm font-medium">Server host<Input value={host} onChange={(e) => setHost(e.target.value)} className="mt-1.5" /></label>
          ) : null}
          <label className="block text-sm font-medium">Username<Req />
            <Input aria-label="Username" value={username} onChange={(e) => setUsername(e.target.value)} className="mt-1.5" />
          </label>
          <label className="block text-sm font-medium">Password<Req />
            <SecretInput label="Password" value={secret} onChange={setSecret} />
          </label>
        </div>
      ) : provider === "graph" ? (
        <div className="space-y-4">
          <OAuthGuide provider="graph" />
          <label className="block text-sm font-medium">Directory (tenant) ID<Req />
            <Input aria-label="Tenant ID" value={tenantId} onChange={(e) => setTenantId(e.target.value)} className="mt-1.5" />
          </label>
          <label className="block text-sm font-medium">Application (client) ID<Req />
            <Input aria-label="Client ID" value={clientId} onChange={(e) => setClientId(e.target.value)} className="mt-1.5" />
          </label>
          <label className="block text-sm font-medium">Account email <span className="font-normal text-fg-muted">(target mailbox)</span><Req />
            <Input aria-label="Account email" value={username} onChange={(e) => setUsername(e.target.value)} className="mt-1.5" />
          </label>
          <label className="block text-sm font-medium">Client secret<Req />
            <SecretInput label="Client secret" value={secret} onChange={setSecret} />
          </label>
        </div>
      ) : (
        <div className="space-y-4">
          <OAuthGuide provider="gmail" />
          <label className="block text-sm font-medium">Account email <span className="font-normal text-fg-muted">(mailbox to read)</span><Req />
            <Input aria-label="Account email" value={username} onChange={(e) => setUsername(e.target.value)} className="mt-1.5" />
          </label>
          <label className="block text-sm font-medium">Service account JSON<Req />
            <textarea aria-label="Service account JSON" value={secret} onChange={(e) => setSecret(e.target.value)} rows={6}
              className="mono mt-1.5 block w-full rounded-md border border-input bg-transparent p-3 text-xs shadow-xs outline-none transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 dark:bg-input/30" />
          </label>
        </div>
      )}

      <p className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
        <ShieldCheck size={14} aria-hidden className="text-success" /> We read mail to migrate it. We never store contents.
      </p>

      <div>
        <Button type="button" variant="outline" onClick={() => void onTest()} disabled={testing}>
          {testing ? "Testing…" : "Test connection"}
        </Button>
      </div>

      {result?.ok ? (
        <p role="status" className="inline-flex items-center gap-1.5 text-success">
          <CheckCircle2 size={16} aria-hidden /> Connected — found {result.folderCount} folders, {result.messageCount.toLocaleString()} messages.
        </p>
      ) : null}
      {result && !result.ok ? (
        <ErrorAlert
          message={failure.message}
          helpLabel={failure.helpLabel} helpHref={failure.helpHref}
          technicalDetail={`${result.errorCode ?? ""} ${result.rawDetail ?? ""}`.trim()}
        />
      ) : null}

      <Button type="button" disabled={!result?.ok} onClick={onContinue} className="block">
        Continue
      </Button>
    </div>
  );
}
