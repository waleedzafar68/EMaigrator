using System;

namespace EMaigrator.Api.Data;

/// <summary>
/// API-owned persistence of one operator-approved resolution: the <see cref="EMaigrator.Core.Diagnostics.RemediationAction"/>
/// name chosen for a given pre-flight issue type on a Job. Lives in <see cref="ApiSideContext"/> (NOT the
/// frozen CONTRACTS §5 schema), so the engine's <c>Job</c>/<c>MailboxMigration</c> shapes are untouched.
/// </summary>
public sealed class ApprovedResolutionRow
{
    /// <summary>Surrogate key — database-generated identity.</summary>
    public long Id { get; set; }

    public Guid JobId { get; set; }

    public string IssueType { get; set; } = "";

    /// <summary>The chosen <see cref="EMaigrator.Core.Diagnostics.RemediationAction"/> name.</summary>
    public string Action { get; set; } = "";
}
