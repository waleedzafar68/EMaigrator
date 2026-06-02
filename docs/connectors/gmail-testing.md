# Gmail Connector — Testing & Scope Notes

## Deferred live-testing risk (DESIGN.md §17)

**Paid Google Workspace live testing is deferred.** Until a real Google migration runs end-to-end,
the Gmail connector is validated **only against recorded fixtures** (WireMock.Net replaying captured
Gmail v1 API responses). This is an accepted, documented risk: the recorded shapes may drift from
live Gmail behavior (label visibility quirks, import edge cases, quota responses). The connector is
not certified against a live tenant for v1.

## Auth & scope

- Auth method: **BYO service account + domain-wide delegation (DWD)** — `AuthMethod.GmailServiceAccountDwd`.
- Config: the impersonated mailbox is supplied via `ConnectionDescriptor.Settings["accountEmail"]`;
  the service-account JSON key is supplied via `SecretBundle.Values["serviceAccountJson"]`.
- **OAuth scope is the single, minimal `https://mail.google.com/`.** No narrower scope authorizes
  both `messages.get?format=raw` (full-fidelity read) and `messages.import` (write with preserved
  internalDate); `gmail.readonly` cannot write, and `gmail.modify` cannot import arbitrary raw mail.
  The broad-but-justified single scope is the **least privilege** that satisfies a non-destructive copy.
- The SA JSON is parsed in-memory only (`GoogleCredential.FromJson`), never written to disk, never
  logged, and held transiently for the lifetime of the provider.

## Recording fresh fixtures from a real tenant

When a Google Workspace test tenant is available:

1. In Google Cloud, create a service account; enable **domain-wide delegation**; in the Workspace
   Admin console authorize the SA client id for scope `https://mail.google.com/` only.
2. Authenticate as the SA impersonating a seeded test mailbox.
3. Capture each response body verbatim and save under `src/EMaigrator.Connectors.Gmail.Tests/Fixtures/`:
   - `GET users/me/labels` → `labels.list.json` (must include system + nested user labels)
   - `GET users/me/messages?labelIds=<id>` → `messages.list.json`
   - `GET users/me/messages/{id}?format=raw` → `messages.get.raw.json`
   - `POST users/me/labels` → `labels.create.json`
   - `POST users/me/messages/import?internalDateSource=dateHeader` → `messages.import.json`
   - A throttled call → `error.429.json` (reason `rateLimitExceeded`)
4. Scrub any real addresses/ids to synthetic values before committing.
5. Re-run `dotnet test src/EMaigrator.Connectors.Gmail.Tests` — all fixture-driven tests must stay green.
