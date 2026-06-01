using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Core.Preflight;

/// <summary>One detected pre-flight issue with a recommended structural remediation (CONTRACTS.md §3).</summary>
public sealed record PreflightIssue(string IssueType, IReadOnlyList<string> AffectedPaths,
    RemediationAction RecommendedAction, IReadOnlyList<RemediationAction> Options, Severity Severity, string Description);
