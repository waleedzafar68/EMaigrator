using System;

namespace EMaigrator.Api.Data;

/// <summary>
/// API-owned persistence of a serialized <see cref="EMaigrator.Core.Preflight.PreflightPlan"/>, keyed by
/// the owning Job id. Lives in <see cref="ApiSideContext"/> (NOT the frozen CONTRACTS §5 schema), so the
/// <c>Job</c>/<c>MailboxMigration</c> shapes are never altered to carry a plan column.
/// </summary>
public sealed class PreflightResultRow
{
    /// <summary>Primary key — the owning Job id.</summary>
    public Guid JobId { get; set; }

    public string PlanJson { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
