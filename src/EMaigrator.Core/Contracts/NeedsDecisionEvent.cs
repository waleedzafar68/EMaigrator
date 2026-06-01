using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Core.Contracts;

/// <summary>Event: a mid-run surprise needing a user decision (CONTRACTS.md §4).</summary>
public sealed record NeedsDecisionEvent(Guid MailboxMigrationId, string IssueType, string Detail, RemediationAction[] Options);
