using System.Collections.Generic;

namespace EMaigrator.Api.Contracts;

/// <summary>A single pre-flight issue surfaced to the operator (Core enums rendered as strings).</summary>
public sealed record PreflightIssueDto(
    string IssueType,
    IReadOnlyList<string> AffectedPaths,
    string RecommendedAction,
    IReadOnlyList<string> Options,
    string Severity,
    string Description);

/// <summary>The pre-flight size/time estimate (duration flattened to seconds for the wire).</summary>
public sealed record MigrationEstimateDto(
    int MailboxCount,
    int FolderCount,
    long MessageCount,
    long TotalBytes,
    double EstimatedDurationSeconds);

/// <summary>
/// The stored pre-flight plan returned by <c>GET /migrations/{id}/preflight</c>. <c>Scanning</c> is true
/// while the background scan is still in flight (Job is in <c>PreFlight</c> and no stored plan row exists
/// yet) — in that case <c>Issues</c> is empty and <c>Estimate</c> is all-zero; once the plan is stored it is
/// false and the real issues/estimate are returned.
/// </summary>
public sealed record PreflightPlanDto(
    IReadOnlyList<PreflightIssueDto> Issues, MigrationEstimateDto Estimate, bool Scanning);
