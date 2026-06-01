namespace EMaigrator.Core.Preflight;

/// <summary>The pre-flight result: issues + estimate. Serves error-detection, quota, and approval (CONTRACTS.md §3).</summary>
public sealed record PreflightPlan(IReadOnlyList<PreflightIssue> Issues, MigrationEstimate Estimate);
