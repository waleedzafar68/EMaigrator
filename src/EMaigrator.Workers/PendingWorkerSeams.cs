using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Workers;

/// <summary>
/// Temporary registrations for the persistence-backed seams the pipeline consumers depend on.
/// Plan 08 (API) supplies the real EF-backed implementations (reading the migration/connection/
/// ledger entities) and removes these. They construct cleanly so the worker host composes and DI
/// validation passes; the startup-invoked lookups return empty (nothing to resume yet) while the
/// per-message seams throw a clear error if a migration is actually enqueued before Plan 08 wires
/// the data layer — failing loud rather than silently doing nothing.
/// </summary>
public static class PendingWorkerSeams
{
    private const string NotWired =
        "This worker seam is a pending placeholder. Plan 08 (API) supplies the EF-backed " +
        "implementation that reads the migration/connection data; until then no migration can run.";

    public static IServiceCollection AddPendingWorkerSeams(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Per-message seams: there is no honest default, so they throw if invoked before Plan 08.
        services.AddSingleton<IMigrationConnectionLookup, PendingMigrationConnectionLookup>();
        services.AddSingleton<IMessageRefLister, PendingMessageRefLister>();
        services.AddSingleton<IMessageHydrator, PendingMessageHydrator>();

        // Lookups that return collections have a safe empty default (and are invoked at startup), so
        // the host starts idle: no approved remediations, no not-done migrations, nothing to resume.
        services.AddSingleton<IRemediationPlanStore, EmptyRemediationPlanStore>();
        services.AddSingleton<IJobMigrationLookup, EmptyJobMigrationLookup>();
        services.AddSingleton<IInterruptedJobLookup, EmptyInterruptedJobLookup>();

        return services;
    }

    private sealed class PendingMigrationConnectionLookup : IMigrationConnectionLookup
    {
        public Task<MigrationConnections> GetAsync(Guid mailboxMigrationId, CancellationToken ct)
            => throw new NotImplementedException(NotWired);
    }

    private sealed class PendingMessageRefLister : IMessageRefLister
    {
        public IAsyncEnumerable<string> ListRefsAsync(ISourceProvider source, FolderPath folder, CancellationToken ct)
            => throw new NotImplementedException(NotWired);
    }

    private sealed class PendingMessageHydrator : IMessageHydrator
    {
        public Task<CanonicalMessage> HydrateAsync(ISourceProvider source, FolderPath folder, string reference, CancellationToken ct)
            => throw new NotImplementedException(NotWired);
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
