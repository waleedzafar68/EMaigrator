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

export type RemediationAction =
  | "None" | "RetryWithBackoff" | "FlattenFolder" | "SanitizeFolderName"
  | "RenameFolder" | "MergeFolder" | "SkipMessage";

export interface MigrationProgressDto {
  migratedCount: number;       // CONTRACTS §4 MigrationProgressEvent.Migrated
  total: number;
  currentFolder: string | null;
  msgPerMin: number;
  status: JobStatus;           // ∈ JobStatus — never a throttling sentinel
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
// UsageDto and the usage/scanning fields are API view-model projections (hosted billing §14 +
// async-preflight §6) layered on the frozen Core PreflightPlan(Issues, Estimate). Owned by
// EMaigrator.Api (Plan 08); the frontend mirrors that wire shape. camelCase; track Plan 08's serializer.
export interface UsageDto {
  used: number;
  quota: number;
  overCapMailboxes: number;
  capGb: number;
}
export interface PreflightPlanDto {
  issues: PreflightIssueDto[];
  estimate: MigrationEstimateDto;
  usage: UsageDto | null;
  scanning: boolean;
}
export interface ApproveRequest { resolutions: Record<string, RemediationAction>; }
export interface NeedsDecisionDto {
  migrationId: string;
  issueType: string;
  detail: string;
  options: RemediationAction[];
}
export interface ResultsDto {
  status: JobStatus;
  migratedCount: number;
  skippedCount: number;
  failedCount: number;
  needsDecision: NeedsDecisionDto[];
  sourceCount: number;
  destCount: number;
  durationSeconds: number;
  logDeletesAt: string;
}
export interface AuditEntryDto {
  subject: string | null;
  messageDate: string;
  sourceFolder: string;
  destFolder: string;
  status: "migrated" | "skipped" | "failed";
  errorCode?: string | null;
}
