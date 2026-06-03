using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Startup;

/// <summary>Finds mailbox migrations of jobs left in Running state (crash/deploy interrupted) that still have not-done ledger items.</summary>
public interface IInterruptedJobLookup
{
    Task<IReadOnlyList<Guid>> GetRunningMigrationsToResumeAsync(CancellationToken ct);
}
