using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Orchestration;

/// <summary>Lists the mailbox migrations of a job whose ledger still has not-done items (resume target).</summary>
public interface IJobMigrationLookup
{
    Task<IReadOnlyList<Guid>> GetNotDoneMigrationsAsync(Guid jobId, CancellationToken ct);
}
