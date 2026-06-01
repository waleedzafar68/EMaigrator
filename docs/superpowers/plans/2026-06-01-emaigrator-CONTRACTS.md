# EMaigrator v1 — Frozen Contracts Reference

> **Status:** FROZEN. The single source of truth for every cross-subsystem signature. Plans 01–10 bind to this verbatim. Changing a signature here is a coordination event: update this file, then every consuming plan.
> **Namespaces** rooted at `EMaigrator.Core` unless noted. Target: **.NET 10**, C# 13, nullable enabled, `record`/`readonly record struct` for value types.

---

## 1. Canonical Model — `EMaigrator.Core.Model`

```csharp
public readonly record struct ProviderId(string Value)   // "imap", "graph", "gmail"
{
    public override string ToString() => Value;
}

// Folder path as a provider-neutral value object. Always stored with '/' canonical separator.
public sealed record FolderPath
{
    public IReadOnlyList<string> Segments { get; }
    public int Depth => Segments.Count;
    public string Name => Segments.Count == 0 ? "" : Segments[^1];
    public FolderPath(IReadOnlyList<string> segments);
    public static FolderPath Parse(string path, char separator = '/');
    public string ToString(char separator);        // join segments
    public override string ToString();              // '/'-joined
    public FolderPath Parent();                     // throws if root
    public bool IsRoot => Segments.Count == 0;
}

[Flags]
public enum MessageFlags { None = 0, Seen = 1, Answered = 2, Flagged = 4, Draft = 8, Deleted = 16 }

public sealed record CanonicalAttachmentInfo(string FileName, string ContentType, long SizeBytes);

// A message NEVER holds its body in a field. Content is opened as a stream on demand
// (streaming pass-through — DESIGN.md §6/§10). The stream yields raw RFC822/MIME bytes.
public sealed record CanonicalMessage
{
    public required string IdentityKey { get; init; }     // see IdentityKey (always set by source provider)
    public string? MessageId { get; init; }               // RFC Message-ID header, may be null
    public required DateTimeOffset InternalDate { get; init; }
    public MessageFlags Flags { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];   // Gmail labels / MS365 categories
    public long SizeBytes { get; init; }
    public IReadOnlyList<CanonicalAttachmentInfo> Attachments { get; init; } = [];
    public string? Subject { get; init; }                 // for logging only (toggleable); never required for copy
    // Opens the raw message content stream. Caller disposes. Bodies transit memory only.
    public required Func<CancellationToken, Task<Stream>> OpenContentAsync { get; init; }
}

public sealed record CanonicalFolder(FolderPath Path, long EstimatedMessageCount, MessageFlags? SpecialUse = null);
```

### Identity key — `EMaigrator.Core.Idempotency`

```csharp
public static class IdentityKey
{
    // Primary: normalized Message-ID. Fallback: composite SHA-256 hex over normalized
    // From|To|Subject|Date|<sha256(decoded body)>. NEVER hashes raw transport bytes.
    public static string Compute(MessageIdentityInput input);   // returns "mid:<...>" or "h:<sha256hex>"
}

public sealed record MessageIdentityInput
{
    public string? MessageId { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset? Date { get; init; }
    public required string DecodedBodySha256Hex { get; init; }   // caller computes over decoded body text
}
```

---

## 2. Provider Abstractions — `EMaigrator.Core.Abstractions`

```csharp
public enum AuthMethod { ImapBasic, ImapOAuthXoauth2, GraphAppOAuth, GraphDelegatedOAuth, GmailServiceAccountDwd, GmailDelegatedOAuth }

public sealed record ProviderConstraints
{
    public int MaxFolderDepth { get; init; } = int.MaxValue;
    public int MaxPathLengthChars { get; init; } = int.MaxValue;
    public IReadOnlyCollection<char> IllegalNameChars { get; init; } = [];
    public long MaxMessageBytes { get; init; } = long.MaxValue;
    public long MaxAttachmentBytes { get; init; } = long.MaxValue;
    public char FolderSeparator { get; init; } = '/';
    public IReadOnlyCollection<string> ReservedFolderNames { get; init; } = [];
}

// Opaque, validated connection settings (host/port/region/auth + a secretRef pointing at ISecretStore).
public sealed record ConnectionDescriptor
{
    public required ProviderId Provider { get; init; }
    public required AuthMethod Auth { get; init; }
    public required IReadOnlyDictionary<string, string> Settings { get; init; }  // non-secret (host, port, region, tenantId, clientId, accountEmail)
    public string? SecretRef { get; init; }   // resolves via ISecretStore to the secret bundle (password / client secret / SA-json)
}

public sealed record ConnectionTestResult(bool Ok, int FolderCount, long MessageCount, string? ErrorCode = null, string? RawDetail = null);

public sealed record ReadOptions
{
    public DateTimeOffset? Since { get; init; }
    public DateTimeOffset? Before { get; init; }
}

public sealed record WriteResult(bool Written, string? DestMessageId = null, string? ErrorCode = null);

public interface ISourceProvider : IAsyncDisposable
{
    ProviderId Id { get; }
    ProviderConstraints Constraints { get; }
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
    Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct);
    IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(FolderPath folder, ReadOptions options, CancellationToken ct);
}

public interface IDestinationProvider : IAsyncDisposable
{
    ProviderId Id { get; }
    ProviderConstraints Constraints { get; }
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
    Task EnsureFolderAsync(FolderPath folder, CancellationToken ct);
    Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct);
    Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct);  // non-empty-dest dedup
}

// Plugin descriptor — DI-discovered (one per connector assembly).
public interface IProviderPlugin
{
    ProviderId Id { get; }
    IReadOnlyCollection<AuthMethod> SupportedAuth { get; }
    bool CanBeSource { get; }
    bool CanBeDestination { get; }
    ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets);
    IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets);
}

public sealed record SecretBundle(IReadOnlyDictionary<string, string> Values);  // decrypted, transient, never logged
```

---

## 3. Error Catalog, Remediation & Pre-flight — `EMaigrator.Core.Diagnostics` / `…Preflight`

```csharp
public enum Severity { Info, Warning, Blocker }

public enum RemediationKind { Transient, Structural }   // Transient = auto-retry; Structural = user decides

public enum RemediationAction
{
    None, RetryWithBackoff,                 // transient
    FlattenFolder, SanitizeFolderName, RenameFolder, MergeFolder, SkipMessage  // structural
}

public sealed record ErrorRule
{
    public ProviderId? Provider { get; init; }            // null = any provider
    public required string SignatureRegex { get; init; }  // matched against normalized error signature
    public required string Diagnosis { get; init; }       // plain-language "what happened"
    public required string Suggestion { get; init; }      // "what to do"; MUST NOT echo credentials
    public required RemediationKind Kind { get; init; }
    public RemediationAction RecommendedAction { get; init; }
    public IReadOnlyList<RemediationAction> Options { get; init; } = [];
    public required Severity Severity { get; init; }
    public string? HelpUrl { get; init; }
}

public sealed record ErrorResolution(ErrorRule Rule, string Diagnosis, string Suggestion,
    RemediationKind Kind, RemediationAction RecommendedAction, IReadOnlyList<RemediationAction> Options, Severity Severity);

public interface IErrorCatalog
{
    ErrorResolution? Match(ProviderId provider, string errorSignature);
}

// Optional AI fallback for the unknown tail (hosted-only; never auto-fixes).
public interface IErrorExplainer { Task<ErrorResolution?> ExplainAsync(ProviderId provider, string errorSignature, CancellationToken ct); }

// Folder transforms (pure).
public static class FolderSanitizer { public static FolderPath Sanitize(FolderPath path, ProviderConstraints c); }
public static class FolderFlattener { public static FolderPath Flatten(FolderPath path, int maxDepth, char joinChar = '-'); }

public sealed record ScopeSpec
{
    public bool IsBatch { get; init; }
    public IReadOnlyList<MailboxPair> Pairs { get; init; } = [];
    public IReadOnlyList<string>? IncludeFolders { get; init; }   // null = all
    public IReadOnlyList<string>? ExcludeFolders { get; init; }
    public DateTimeOffset? Since { get; init; }
    public DateTimeOffset? Before { get; init; }
}
public sealed record MailboxPair(string SourceMailbox, string DestMailbox);

public sealed record PreflightIssue(string IssueType, IReadOnlyList<string> AffectedPaths,
    RemediationAction RecommendedAction, IReadOnlyList<RemediationAction> Options, Severity Severity, string Description);

public sealed record MigrationEstimate(int MailboxCount, int FolderCount, long MessageCount, long TotalBytes, TimeSpan EstimatedDuration);

public sealed record PreflightPlan(IReadOnlyList<PreflightIssue> Issues, MigrationEstimate Estimate);

public interface IPreflightAnalyzer
{
    Task<PreflightPlan> AnalyzeAsync(ISourceProvider source, IDestinationProvider dest, ScopeSpec scope, CancellationToken ct);
}
```

---

## 4. Ledger, Secrets, Rate Limiter, Orchestrator — `EMaigrator.Core.Abstractions`

```csharp
public enum LedgerStatus { Pending, Migrated, Skipped, Failed }

public sealed record LedgerEntry(Guid MailboxMigrationId, string IdentityKey, string SourceFolder,
    string DestFolder, LedgerStatus Status, string? ErrorCode, DateTimeOffset UpdatedAt);

public interface ILedger
{
    Task<bool> IsDoneAsync(Guid mailboxMigrationId, string identityKey, CancellationToken ct);   // Migrated|Skipped
    Task MarkAsync(Guid mailboxMigrationId, string identityKey, string sourceFolder, string destFolder, LedgerStatus status, string? errorCode, CancellationToken ct);
    IAsyncEnumerable<LedgerEntry> GetNotDoneAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task<LedgerCounts> GetCountsAsync(Guid mailboxMigrationId, CancellationToken ct);
}
public sealed record LedgerCounts(long Migrated, long Skipped, long Failed, long Pending);

public interface ISecretStore
{
    Task<string> StoreAsync(string tenantId, string plaintext, CancellationToken ct);   // returns secretRef
    Task<string> RetrieveAsync(string secretRef, CancellationToken ct);                  // transient plaintext
    Task PurgeAsync(string secretRef, CancellationToken ct);
}

public readonly record struct RateLimitKey(ProviderId Provider, string Account);

public interface IRateLimiter
{
    Task<bool> TryAcquireAsync(RateLimitKey key, int tokens, CancellationToken ct);   // false = throttled, caller backs off
    Task PenalizeAsync(RateLimitKey key, TimeSpan retryAfter, CancellationToken ct);  // honor Retry-After / 429
}

public interface IJobOrchestrator
{
    Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task RequestPauseAsync(Guid jobId, CancellationToken ct);
    Task RequestResumeAsync(Guid jobId, CancellationToken ct);
    Task RequestCancelAsync(Guid jobId, CancellationToken ct);
}
```

### MassTransit message contracts — `EMaigrator.Core.Contracts`

```csharp
public sealed record StartMigration(Guid MailboxMigrationId);
public sealed record MigrateFolder(Guid MailboxMigrationId, Guid FolderTaskId, string SourceFolder, string DestFolder);
public sealed record MigrateBatch(Guid MailboxMigrationId, Guid FolderTaskId, string SourceFolder, string DestFolder, IReadOnlyList<string> SourceMessageRefs);
public sealed record MigrationProgressEvent(Guid MailboxMigrationId, long Migrated, long Total, string? CurrentFolder, double MsgPerMin, string Status); // Status ∈ JobStatus
public sealed record NeedsDecisionEvent(Guid MailboxMigrationId, string IssueType, string Detail, RemediationAction[] Options);
```

---

## 5. Persistence Entities — `EMaigrator.Infrastructure.Data` (shapes; EF Core, PostgreSQL)

```csharp
public enum JobStatus { Draft, Queued, PreFlight, AwaitingApproval, Running, Paused, Completed, Partial, Failed, Cancelled }
public enum MailboxMigrationStatus { Pending, Running, Completed, Partial, Failed, Cancelled }

public class Job        { Guid Id; Guid TenantId; ProviderId SourceProvider; ProviderId DestProvider;
                          string? SourceConnectionRef; string? DestConnectionRef; bool IsBatch;
                          JobStatus Status; int WizardStep; bool StoreSubjects; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; }
public class MailboxMigration { Guid Id; Guid JobId; string SourceMailbox; string DestMailbox; MailboxMigrationStatus Status;
                          long MigratedCount; long SkippedCount; long FailedCount; DateTimeOffset? StartedAt; DateTimeOffset? FinishedAt; } // billing unit
public class FolderTask  { Guid Id; Guid MailboxMigrationId; string SourceFolder; string DestFolder; string Status; }
public class LedgerEntryRow { long Id; Guid MailboxMigrationId; string IdentityKey; string SourceFolder; string DestFolder;
                          LedgerStatus Status; string? ErrorCode; DateTimeOffset UpdatedAt; }   // UNIQUE(MailboxMigrationId, IdentityKey). NO body, NO subject.
public class MigrationLogRow { long Id; Guid MailboxMigrationId; string? Subject; DateTimeOffset MessageDate; string SourceFolder;
                          string DestFolder; string Status; string? ErrorCode; DateTimeOffset CreatedAt; }   // encrypted at rest; 30-day purge; NO sender/recipient
public class CredentialRow { Guid Id; Guid TenantId; string SecretRef; string CipherBlob; DateTimeOffset CreatedAt; }  // purged on job-terminal
public class Tenant { Guid Id; string Name; }
// ApplicationUser : IdentityUser<Guid> { Guid TenantId; }  — ASP.NET Core Identity
```

**Invariants enforced by tests:** `LedgerEntryRow` and `MigrationLogRow` have **no** column for body/attachment content; `MigrationLogRow` has no sender/recipient; subject is nullable and omitted when `Job.StoreSubjects == false`.

---

## 6. REST API Contract — `EMaigrator.Api` (base `/api/v1`, all routes require auth except `/health`)

| Method & Route | Body / Query | Returns |
|---|---|---|
| `POST /migrations` | `{ }` | `MigrationDto` (new Draft) |
| `GET /migrations` | `?status=&q=` | `MigrationDto[]` |
| `GET /migrations/{id}` | — | `MigrationDto` |
| `DELETE /migrations/{id}` | — | 204 (discard draft / cancel) |
| `PATCH /migrations/{id}/endpoints` | `SetEndpointsRequest{ from:ProviderId, to:ProviderId }` | `MigrationDto` |
| `PUT /migrations/{id}/connection/{side}` | `ConnectionRequest{ auth, settings, secret }` (`side`∈`from`\|`to`) | `MigrationDto` |
| `POST /migrations/{id}/connection/{side}/test` | — | `ConnectionTestResult` |
| `PUT /migrations/{id}/scope` | `ScopeRequest` (mirrors `ScopeSpec`; CSV upload via `multipart`) | `MigrationDto` |
| `POST /migrations/{id}/preflight` | — | 202 (async) → progress via SignalR; result `GET /preflight` |
| `GET /migrations/{id}/preflight` | — | `PreflightPlanDto` |
| `POST /migrations/{id}/approve` | `ApproveRequest{ resolutions: {issueType→action} }` | `MigrationDto` (→Running) |
| `POST /migrations/{id}/pause` \| `/resume` \| `/cancel` | — | `MigrationDto` |
| `GET /migrations/{id}/results` | — | `ResultsDto` (counts, reconciliation, needs-decision[]) |
| `GET /migrations/{id}/audit` | `?q=&failuresOnly=` | `AuditEntryDto[]` |
| `POST /migrations/{id}/rerun` | — | `MigrationDto` (re-enqueue not-done) |
| `GET /migrations/{id}/report` | `?format=csv\|pdf` | file stream |
| `GET /health` | — | health JSON (public) |

DTOs are camelCase JSON. `MigrationDto` carries `{ id, status, wizardStep, from, to, isBatch, scopeSummary, mailboxCount, progress, createdAt }`.

### SignalR hub — `/hubs/migrations`

```csharp
public interface IMigrationProgressClient            // server → client
{
    Task Progress(MigrationProgressDto dto);
    Task StatusChanged(string migrationId, string status);
    Task NeedsDecision(string migrationId, NeedsDecisionDto dto);
}
public class MigrationsHub : Hub<IMigrationProgressClient>   // client → server
{
    public Task Subscribe(string migrationId);   // group = migrationId; tenant-authorized
    public Task Unsubscribe(string migrationId);
}
```

---

## 7. Configuration Options — `EMaigrator.Core.Configuration`

```csharp
public sealed class OrchestrationOptions { public int GlobalMaxConcurrentMigrations { get; set; } = 16;
    public int PerTenantConcurrencyCap { get; set; } = 8; public int PerMailboxFolderConcurrency { get; set; } = 4;
    public int BatchSize { get; set; } = 100; public int ConsumerPrefetch { get; set; } = 16;
    public int DlqRetryCount { get; set; } = 5; }
public sealed class RateLimitOptions { public Dictionary<string, BucketSpec> Buckets { get; set; } = new(); }  // key "provider:account-class"
public sealed record BucketSpec { public double RefillPerSecond { get; init; } public int Burst { get; init; } }
public sealed class RetentionOptions { public int LogRetentionDays { get; set; } = 30; }
public sealed class SecretStoreOptions { public string Mode { get; set; } = "LocalKey"; /* or "AzureKeyVault"|"AwsKms" */ public string? KeyRef { get; set; } }
```

---

## 8. Naming & conventions (so all plans agree)

- Solution `EMaigrator.sln`; projects exactly as `DESIGN.md §15`. Test projects: `<Project>.Tests` (unit) + `<Project>.IntegrationTests` where applicable.
- Async methods end `Async`; `CancellationToken ct` is the last parameter, always passed through.
- One public type per file; file name = type name. Connector assemblies expose exactly one `IProviderPlugin` registered via a `IServiceCollection` extension `Add<Name>Connector()`.
- Errors normalize to a stable `errorSignature` string (provider code + condition) before catalog matching — connectors own the normalization.
- Frontend mirrors these DTOs in `web/src/api/types.ts`; SignalR event names match the hub method names exactly.
