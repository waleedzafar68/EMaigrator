using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using MassTransit;

namespace EMaigrator.Workers.IntegrationTests;

/// <summary>
/// Fixed source/dest connection descriptors for ONE migration against the shared GreenMail
/// server (src@ and dst@ are distinct GreenMail accounts, so source and dest folders never
/// collide even though the empty-remediation path resolver keeps dest folder == source folder).
/// </summary>
public sealed class TestConnectionLookup : IMigrationConnectionLookup
{
    private readonly MigrationConnections _conns;
    private readonly Guid _migrationId;

    public TestConnectionLookup(Guid migrationId, MigrationConnections conns)
    {
        _migrationId = migrationId;
        _conns = conns;
    }

    public Task<MigrationConnections> GetAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        if (mailboxMigrationId != _migrationId)
        {
            throw new InvalidOperationException(
                $"Unexpected migration id {mailboxMigrationId}; fixture only knows {_migrationId}.");
        }

        return Task.FromResult(_conns);
    }
}

/// <summary>
/// Wraps the real <see cref="ImapMessageHydrator"/> (production, EMaigrator.Workers.Sessions).
/// When poison mode is on, any message whose
/// subject carries the <see cref="PoisonMarker"/> throws AFTER hydration — standing in for the
/// "oversize / unconvertible" long-tail that must DLQ rather than wedge the folder.
/// </summary>
public sealed class FaultInjectingMessageHydrator : IMessageHydrator
{
    public const string PoisonMarker = "EMAIGRATOR_POISON";

    private readonly ImapMessageHydrator _inner = new();

    /// <summary>Static so the bus-resolved singleton consumer and the test toggle the same flag.</summary>
    public static bool PoisonEnabled { get; set; }

    public async Task<CanonicalMessage> HydrateAsync(
        ISourceProvider source, FolderPath folder, string reference, CancellationToken ct)
    {
        var message = await _inner.HydrateAsync(source, folder, reference, ct).ConfigureAwait(false);
        if (PoisonEnabled && message.Subject is { } subject &&
            subject.Contains(PoisonMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Simulated poison (oversize stand-in) for DLQ verification.");
        }

        return message;
    }
}

/// <summary>No pre-flight remediations: dest folder == source folder.</summary>
public sealed class EmptyRemediationStore : IRemediationPlanStore
{
    public Task<IReadOnlyList<ApprovedRemediation>> GetApprovedAsync(Guid mailboxMigrationId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ApprovedRemediation>>(Array.Empty<ApprovedRemediation>());
}

/// <summary>No job-scoped resume fan-out is needed for these single-migration tests.</summary>
public sealed class EmptyJobMigrationLookup : IJobMigrationLookup
{
    public Task<IReadOnlyList<Guid>> GetNotDoneMigrationsAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
}

/// <summary>
/// On host startup, returns whatever migration ids the fixture has armed for resume. The
/// crash-resume test arms the running migration before rebuilding the host so the startup
/// service re-enqueues StartMigration; ledger IsDone makes the re-fan-out idempotent.
/// </summary>
public sealed class TestInterruptedJobLookup : IInterruptedJobLookup
{
    /// <summary>Shared with the fixture; the bus resolves this as a singleton.</summary>
    public static readonly List<Guid> ToResume = new();

    public Task<IReadOnlyList<Guid>> GetRunningMigrationsToResumeAsync(CancellationToken ct)
    {
        lock (ToResume)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(ToResume.ToArray());
        }
    }
}

/// <summary>Captures every NeedsDecisionEvent the pipeline publishes (DLQ verification).</summary>
public sealed class CollectingNeedsDecisionConsumer : IConsumer<NeedsDecisionEvent>
{
    public static readonly ConcurrentBag<NeedsDecisionEvent> Decisions = new();

    public Task Consume(ConsumeContext<NeedsDecisionEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Decisions.Add(context.Message);
        return Task.CompletedTask;
    }
}
