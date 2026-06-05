// Mirrors EMaigrator v1 CONTRACTS §3/§4/§6 — camelCase JSON. Do not invent fields.
export type ProviderId = "imap" | "graph" | "gmail";
export type ConnectionSide = "from" | "to";

export type JobStatus =
  | "Draft" | "Queued" | "PreFlight" | "AwaitingApproval" | "Running"
  | "Paused" | "Completed" | "Partial" | "Failed" | "Cancelled";

export type AuthMethod =
  | "ImapBasic" | "ImapOAuthXoauth2" | "GraphAppOAuth" | "GraphDelegatedOAuth"
  | "GmailServiceAccountDwd" | "GmailDelegatedOAuth";

export type Severity = "Info" | "Warning" | "Blocker";

// GET /providers — capability matrix the API is authoritative for (CONTRACTS §3). camelCase.
export interface ProviderCapabilityDto {
  id: ProviderId;
  canBeSource: boolean;
  canBeDestination: boolean;
  canBatch: boolean;
  supportedAuth: AuthMethod[];
}

export type RemediationAction =
  | "None" | "RetryWithBackoff" | "FlattenFolder" | "SanitizeFolderName"
  | "RenameFolder" | "MergeFolder" | "SkipMessage";

// One type covers BOTH wire shapes (API canonical, camelCase):
//   • REST   MigrationProgressSummary { migrated, total, percent, currentFolder, msgPerMin } — no migrationId, no status.
//   • SignalR MigrationProgressDto    { migrationId, migrated, total, currentFolder, msgPerMin, status } — no percent.
// `migrated`, `total`, `currentFolder`, `msgPerMin` are common; the side-specific fields are optional.
export interface MigrationProgressDto {
  migrated: number;            // API MigrationProgressSummary.Migrated / MigrationProgressDto.Migrated
  total: number;
  currentFolder: string | null;
  msgPerMin: number;
  migrationId?: string;        // SignalR push only — lets multi-row listeners route to the right row
  percent?: number;            // REST summary only — prefer it when present, else compute migrated/total
  status?: JobStatus;          // SignalR push only — ∈ JobStatus, never a throttling sentinel
  // throttling is NOT a JobStatus (CONTRACTS freezes Status ∈ JobStatus); rides a dedicated
  // optional flag the Api (Plan 08) sets from the rate-limiter. Absent/false ⇒ not throttled.
  throttled?: boolean;
}

export interface MigrationDto {
  id: string;
  status: JobStatus;
  wizardStep: number;
  from: ProviderId | null;
  to: ProviderId | null;
  isBatch: boolean;
  scopeSummary: string | null;
  mailboxCount: number;
  progress: MigrationProgressDto | null;
  createdAt: string;
}

export interface ConnectionTestResult {
  ok: boolean;
  folderCount: number;
  messageCount: number;
  errorCode?: string | null;
  rawDetail?: string | null;
}

export interface SetEndpointsRequest { from: ProviderId; to: ProviderId; }
export interface ConnectionRequest {
  auth: AuthMethod;
  settings: Record<string, string>;
  secret: string;
}
export interface MailboxPairDto { sourceMailbox: string; destMailbox: string; }
export interface ScopeRequest {
  isBatch: boolean;
  pairs: MailboxPairDto[];
  includeFolders?: string[] | null;
  excludeFolders?: string[] | null;
  since?: string | null;
  before?: string | null;
}
export interface PreflightIssueDto {
  issueType: string;
  affectedPaths: string[];
  recommendedAction: RemediationAction;
  options: RemediationAction[];
  severity: Severity;
  description: string;
}
export interface MigrationEstimateDto {
  mailboxCount: number;
  folderCount: number;
  messageCount: number;
  totalBytes: number;
  estimatedDurationSeconds: number;
}
// UsageDto is a hosted-layer view-model projection (hosted billing §14). The OSS API's
// GET /migrations/{id}/preflight serializes { issues, estimate, scanning } — usage is intentionally
// ABSENT in OSS, so it stays optional here and renders gracefully when missing. camelCase; do not
// invent fields.
export interface UsageDto {
  used: number;
  quota: number;
  overCapMailboxes: number;
  capGb: number;
}
export interface PreflightPlanDto {
  issues: PreflightIssueDto[];
  estimate: MigrationEstimateDto;
  // API PreflightPlanDto.Scanning (always sent): true while the background scan is in flight
  // (Job.Status == PreFlight and no stored plan yet) → issues/estimate are empty/zeroed; false once
  // the stored plan exists. Poll GET /preflight while this is true. (CONTRACTS §6 async-preflight)
  scanning: boolean;
  usage?: UsageDto | null;   // hosted-only: never sent by the OSS preflight endpoint
}
export interface ApproveRequest { resolutions: Record<string, RemediationAction>; }
// Mirrors BOTH the API's REST NeedsDecisionItemDto and SignalR NeedsDecisionDto: { issueType, detail,
// options } — options are RemediationAction names serialized as plain strings, with NO migrationId in
// the payload (SignalR passes the id as a separate hub-method argument). (CONTRACTS §6)
export interface NeedsDecisionDto {
  issueType: string;
  detail: string;
  options: RemediationAction[];
}
// Mirrors the API ResultsDto: nested counts + reconciliation + the needs-decision queue, plus the
// job's status and (when computable) duration + log-retention deadline. camelCase; do not invent fields.
export interface ResultCounts {
  migrated: number;
  skipped: number;
  failed: number;
}
export interface Reconciliation {
  sourceCount: number;
  destCount: number;
  matched: boolean;
}
export interface ResultsDto {
  counts: ResultCounts;
  reconciliation: Reconciliation;
  needsDecision: NeedsDecisionDto[];
  status: JobStatus;            // API ResultsDto.Status — the Job's status (e.g. "Completed"|"Partial"|...)
  // API ResultsDto.DurationSeconds: (max FinishedAt - min StartedAt) across the job's MailboxMigrations,
  // in seconds; null while not all mailboxes have both started and finished.
  durationSeconds: number | null;
  // API ResultsDto.LogDeletesAt: ISO timestamp = latest MigrationLog.CreatedAt + LogRetentionDays
  // (default 30); null when there are no log rows yet.
  logDeletesAt: string | null;
}
export interface AuditEntryDto {
  subject: string | null;
  date: string;                // API AuditEntryDto.Date (DateTimeOffset → ISO string)
  sourceFolder: string;
  destFolder: string;
  status: "migrated" | "skipped" | "failed";
  errorCode?: string | null;
}
