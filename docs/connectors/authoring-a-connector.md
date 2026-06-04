# Authoring a connector

A connector is a self-contained assembly (`EMaigrator.Connectors.<Name>`) that adapts one provider's
SDK to the Core abstractions. It references **only `EMaigrator.Core`** (the dependency rule — enforced
by NetArchTest). Adding a provider touches **no Core code and no other connector**: you implement one
plugin, register it, and wire it into the three composition roots.

The three existing connectors (`Imap`, `Graph`, `Gmail`) are the reference implementations; this is the
shape they all share.

## 1. The file skeleton (~10 files)

| File | Role |
|---|---|
| `<Name>ProviderPlugin : IProviderPlugin` | The one discoverable entry point. Declares `Id`, `SupportedAuth`, `CanBeSource`/`CanBeDestination`, and `CreateSource`/`CreateDestination`. Stateless DI **singleton**. |
| `<Name>SourceProvider : ISourceProvider` | Reads folders/messages → `CanonicalMessage`. Short-lived, `IAsyncDisposable`. |
| `<Name>DestinationProvider : IDestinationProvider` | Writes `CanonicalMessage` → provider. Short-lived, `IAsyncDisposable`. |
| `<Name>ConnectionConfig` (+ `Settings`) | Parses & validates `descriptor.Settings`, pulls the secret from `SecretBundle`. **Redacted `ToString`, no disk cache.** |
| `<Name>ClientFactory` / `<Name>ServiceFactory` | Builds the vendor client with the **minimal scope** and an **in-memory-only** token. |
| `<Name>MessageMapper` + folder/label + flag mappers | **Pure static** vendor↔canonical translation. Unit-testable with no network. |
| `<Name>Constraints` (`ProviderConstraints`) | Folder depth / path-length / reserved-name limits used by preflight. |
| `<Name>ErrorNormalizer` (+ a normalized-error type) | Maps a vendor failure to a **credential-free, stable signature**. |
| `<Name>ConfigurationException` | Thrown on bad descriptor/settings. |
| `ServiceCollectionExtensions.Add<Name>Connector()` | DI registration (below). |

Each connector also ships a **DI-registration + contract-conformance test** (proves
`Add<Name>Connector()` registers exactly one plugin with the declared `Id`/auth/capabilities), pure
mapper unit tests, and an HTTP-fixture integration suite (GreenMail for IMAP, WireMock for Graph/Gmail).

## 2. Registration + selection

Expose exactly one plugin and register it with `TryAddEnumerable` so every connector **appends** into
`IEnumerable<IProviderPlugin>` (never replaces):

```csharp
public static IServiceCollection Add<Name>Connector(this IServiceCollection services)
{
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderPlugin, <Name>ProviderPlugin>());
    return services;
}
```

The host resolves all plugins and selects by `ProviderId.Value` (`ProviderSessionFactory` /
`ConnectionService`). **Register `Add<Name>Connector()` in ALL THREE composition roots:**

- `EMaigrator.Workers/Program.cs`
- `EMaigrator.Cli/Hosting/CliHostBuilder.cs`
- `EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`

## 3. Non-negotiable invariants

- **No body persistence.** Never put body bytes on a persisted field. Set
  `CanonicalMessage.OpenContentAsync` to a deferred `Func<CancellationToken, Task<Stream>>` the
  destination opens and disposes on demand.
- **Populate `IdentityKey` on every read** via the shared `Core/Idempotency/IdentityKey.Compute`
  (normalized `Message-ID`, else SHA-256 over headers + the **decoded-body** fingerprint — never raw
  transport bytes). The ledger dedups on it; skipping it breaks resumability.
- **Error signatures are credential-free.** Derive the signature **only** from the exception type /
  HTTP status / a *closed* vendor error-code vocabulary — **never** free-text message (it can embed a
  password or bearer token). This is asserted by the per-connector security gate.
- **Throttle: surface, don't throw (write path).** On a `429`, return a `WriteResult(false, …)` with a
  credential-free signature (carrying `Retry-After` where available) so the worker's rate-limiter backs
  off, rather than faulting the batch. (The connect path may rethrow a sanitized transport error.)
- **Least-privilege credentials.** Pin the minimal scope; keep secrets/tokens in process memory only;
  redact `ToString`.
- **Validate operator-supplied hosts before connecting (anti-SSRF).** Any connector that dials a
  user-supplied host (custom IMAP/SMTP) must replicate `ImapHostValidator` — block loopback /
  link-local / `169.254` metadata literals unless an explicit opt-in is set — *before* opening the
  socket.

## 4. Secret bundle + settings keys (per provider)

Secrets are stored as **connector-shaped JSON** (a flat `Dictionary<string,string>`), not a raw scalar.
`ProviderSessionFactory` deserializes the stored secret into `SecretBundle.Values`, and the
connect-test / preflight / run paths all read it the same way. **Storing the wrong key passes
fake-backed unit tests but fails the real run with an auth error** (this was a real Plan-09 bug). See
also the secret-bundle note in `CONTRACTS.md §4`.

| Provider | `descriptor.Settings` keys (non-secret) | `SecretBundle.Values` keys (secret) |
|---|---|---|
| IMAP (basic) | `accountEmail`, `preset` (+ `region` \| `host`, `useSsl`, `allowPlaintext`, `port`) | `password` |
| IMAP (XOAUTH2) | `accountEmail`, `preset` (…) | `accessToken` |
| Graph | `tenantId`, `clientId`, `accountEmail` | `clientSecret` |
| Gmail (service-account DWD) | `accountEmail` | `serviceAccountJson` |

## 5. Testing notes (per connector)

- **IMAP / GreenMail (Testcontainers).** GreenMail uses a flat `.` folder separator, has no `NAMESPACE`
  command, drops the connection on folder `DELETE` (reset via `EXPUNGE`), and needs auth **enforced** +
  seeding via its management REST API. Wait for readiness on the log line
  `UntilMessageIsLogged("Starting GreenMail API server")`, **not** `UntilPortIsAvailable`. MailKit 4.16
  drifted (`AppendRequest`, nullable returns).
- **Graph / WireMock.** MIME import goes through `ToPostRequestInformation` + `SetStreamContent` because
  the typed `PostAsync` only sends JSON.
- **Gmail / WireMock.** Fixtures only (paid-Workspace live testing is deferred). The import must
  re-append `internalDateSource=dateHeader` on the wire because the SDK omits default-valued query
  params. See [`gmail-testing.md`](gmail-testing.md).
