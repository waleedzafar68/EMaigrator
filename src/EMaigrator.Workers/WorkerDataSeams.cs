using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Persistence;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Workers;

/// <summary>
/// Registers the real EF/IMAP-backed worker data-seams (replaces the throwing PendingWorkerSeams).
/// The three collection-returning lookups (remediations / job-migrations / interrupted-jobs) keep
/// safe empty defaults: a self-host single-node run has no approved remediations, and job-level
/// resume / crash-resume fan-out are not exercised by the CLI's migration-level enqueue path.
/// </summary>
public static class WorkerDataSeams
{
    public static IServiceCollection AddWorkerDataSeams(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMigrationConnectionLookup, EfMigrationConnectionLookup>();
        services.AddSingleton<IMessageRefLister, ImapMessageRefLister>();
        services.AddSingleton<IMessageHydrator, ImapMessageHydrator>();
        services.AddSingleton<IMigrationStatusWriter, EfMigrationStatusWriter>();

        services.AddSingleton<IRemediationPlanStore, EmptyRemediationPlanStore>();
        services.AddSingleton<IJobMigrationLookup, EmptyJobMigrationLookup>();
        services.AddSingleton<IInterruptedJobLookup, EmptyInterruptedJobLookup>();

        return services;
    }

    private sealed class EmptyRemediationPlanStore : IRemediationPlanStore
    {
        public Task<IReadOnlyList<ApprovedRemediation>> GetApprovedAsync(Guid mailboxMigrationId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ApprovedRemediation>>(Array.Empty<ApprovedRemediation>());
    }

    private sealed class EmptyJobMigrationLookup : IJobMigrationLookup
    {
        public Task<IReadOnlyList<Guid>> GetNotDoneMigrationsAsync(Guid jobId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    private sealed class EmptyInterruptedJobLookup : IInterruptedJobLookup
    {
        public Task<IReadOnlyList<Guid>> GetRunningMigrationsToResumeAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }
}
