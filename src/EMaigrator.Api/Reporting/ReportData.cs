using System;
using System.Collections.Generic;

namespace EMaigrator.Api.Reporting;

/// <summary>One destination-folder row in the report's per-folder breakdown.</summary>
public sealed record FolderBreakdownRow(string Folder, long Migrated, long Skipped, long Failed);

/// <summary>
/// The provider-agnostic data the report builders render: the migration's identity, source/destination
/// providers, status, total counts, wall-clock duration, and the per-destination-folder breakdown.
/// </summary>
public sealed record ReportData(
    Guid MigrationId,
    string From,
    string To,
    string Status,
    long Migrated,
    long Skipped,
    long Failed,
    TimeSpan Duration,
    IReadOnlyList<FolderBreakdownRow> Folders);
