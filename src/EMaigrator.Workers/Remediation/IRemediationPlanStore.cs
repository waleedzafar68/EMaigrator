using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Remediation;

/// <summary>
/// Supplies the structural remediations the operator approved at pre-flight (DESIGN.md §7).
/// Implemented in Infrastructure over the persisted approval; faked in unit tests.
/// </summary>
public interface IRemediationPlanStore
{
    Task<IReadOnlyList<ApprovedRemediation>> GetApprovedAsync(Guid mailboxMigrationId, CancellationToken ct);
}
