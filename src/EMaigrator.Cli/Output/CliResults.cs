using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Cli.Output;

public sealed record ConnectTestOutput(bool Ok, int FolderCount, long MessageCount, string? ErrorCode);

public sealed record PreflightIssueOutput(
    string IssueType, Severity Severity, RemediationAction RecommendedAction,
    IReadOnlyList<string> AffectedPaths, string Description);

public sealed record EstimateOutput(int MailboxCount, int FolderCount, long MessageCount, long TotalBytes);

public sealed record PreflightOutput(IReadOnlyList<PreflightIssueOutput> Issues, EstimateOutput Estimate);

public sealed record RunOutput(
    string MailboxMigrationId, long Migrated, long Skipped, long Failed, long Pending, string Status);

public sealed record StatusOutput(
    string MailboxMigrationId, string Status, long Migrated, long Skipped, long Failed, long Pending);
