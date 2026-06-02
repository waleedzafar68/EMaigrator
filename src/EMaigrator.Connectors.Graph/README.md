# EMaigrator.Connectors.Graph

Microsoft Graph connector (source + destination) for Microsoft 365 mailboxes. Implements
`ISourceProvider`, `IDestinationProvider`, and `IProviderPlugin` from `EMaigrator.Core`
(see CONTRACTS.md §2).

## BYO-OAuth setup (v1 — no shared branded app; DESIGN.md §11)

The operator registers **their own** Azure App Registration:

1. Azure Portal → Entra ID → App registrations → New registration.
2. API permissions → Microsoft Graph → **Application permissions** → add **`Mail.ReadWrite`**
   (and nothing else — least privilege; we do **not** request the send permission).
3. Click **Grant admin consent** for the tenant.
4. Certificates & secrets → New client secret → copy the value.

## Connection configuration

`ConnectionDescriptor.Settings` (non-secret):

| Key | Value |
|---|---|
| `tenantId` | Directory (tenant) ID |
| `clientId` | Application (client) ID |
| `accountEmail` | UPN of the mailbox to read/write |

Secret bundle: `{ "clientSecret": "<the app client secret>" }`, stored via `ISecretStore`
and resolved transiently. **The client secret and access tokens are never logged.**

## Security posture

- **Least privilege:** only the `Mail.ReadWrite` application permission is exercised, requested
  via the `https://graph.microsoft.com/.default` scope. We do **not** request the send
  permission, and we do not request broad all-mailbox read beyond what the BYO app was
  consented for.
- **Token cache is in-memory only** — `ClientSecretCredentialOptions.TokenCachePersistenceOptions`
  is left null, so no token is ever persisted to disk.
- **Throttling (429)** is normalized to the credential-free signature `graph:429:throttled` with
  the honored `Retry-After`; tenant identifiers never appear in user-facing error codes.

## Testing

- **Unit + contract + connector tests** run per-commit against **WireMock.Net** fixtures shaped
  like real Graph responses (folders list, message list, MIME `$value`, create message,
  throttling 429 with `Retry-After`). Excluded from live calls.
- **Live smoke** (`GraphLiveSmokeTests`) runs **only** when the `EMAIGRATOR_GRAPH_*` environment
  variables are set, against the **free [Microsoft 365 Developer Program](https://developer.microsoft.com/microsoft-365/dev-program) tenant**.
  It is gated/nightly, **excluded from coverage %**, and skipped by default (never per-commit).

  ```bash
  EMAIGRATOR_GRAPH_TENANT_ID=... \
  EMAIGRATOR_GRAPH_CLIENT_ID=... \
  EMAIGRATOR_GRAPH_CLIENT_SECRET=... \
  EMAIGRATOR_GRAPH_ACCOUNT_EMAIL=user@yourtenant.onmicrosoft.com \
  dotnet test src/EMaigrator.Connectors.Graph.Tests --filter FullyQualifiedName~GraphLiveSmokeTests
  ```
