using System;
using System.Collections.Generic;

namespace EMaigrator.Api.Contracts;

/// <summary>Aggregated per-message outcome counts across a job's mailboxes.</summary>
public sealed record ResultCounts(long Migrated, long Skipped, long Failed);

/// <summary>
/// Source↔destination reconciliation. <c>SourceCount</c> is every message seen; <c>DestCount</c> is the
/// number successfully written; <c>Matched</c> is true when source = dest + skipped + failed (no message
/// is unaccounted for).
/// </summary>
public sealed record Reconciliation(long SourceCount, long DestCount, bool Matched);

/// <summary>A failed/blocked item surfaced for operator resolution, with the actions the operator may pick.</summary>
public sealed record NeedsDecisionItemDto(string IssueType, string Detail, IReadOnlyList<string> Options);

/// <summary>
/// The results payload: counts + reconciliation + the needs-decision queue, plus the job's terminal/running
/// <c>Status</c>, the wall-clock <c>DurationSeconds</c> (max FinishedAt − min StartedAt across the job's
/// mailboxes; null until every mailbox has both timestamps), and <c>LogDeletesAt</c> (the latest
/// <c>MigrationLogRow.CreatedAt</c> for this job plus <c>RetentionOptions.LogRetentionDays</c>; null when no
/// log rows exist yet). JSON key order: counts, reconciliation, needsDecision, status, durationSeconds,
/// logDeletesAt.
/// </summary>
public sealed record ResultsDto(
    ResultCounts Counts,
    Reconciliation Reconciliation,
    IReadOnlyList<NeedsDecisionItemDto> NeedsDecision,
    string Status,
    double? DurationSeconds,
    DateTimeOffset? LogDeletesAt);

/// <summary>
/// One audit row projected from a <c>MigrationLogRow</c>. <c>Subject</c> is null when the job's privacy
/// toggle (<c>StoreSubjects==false</c>) hides subjects. No body, sender, or recipient (DESIGN §10).
/// </summary>
public sealed record AuditEntryDto(
    string? Subject, DateTimeOffset Date, string SourceFolder, string DestFolder, string Status, string? ErrorCode);
