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

/// <summary>The stored pre-flight plan returned by <c>GET /migrations/{id}/preflight</c>.</summary>
public sealed record PreflightPlanDto(IReadOnlyList<PreflightIssueDto> Issues, MigrationEstimateDto Estimate);
