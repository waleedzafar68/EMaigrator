namespace EMaigrator.Core.Abstractions;

/// <summary>Queue/worker orchestration seam (MassTransit vs future Temporal) (CONTRACTS.md §4).</summary>
public interface IJobOrchestrator
{
    Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct);

    /// <summary>Enqueue a reconcile/repair run for one mailbox (publishes ReconcileMailbox).</summary>
    Task EnqueueReconcileAsync(Guid mailboxMigrationId, CancellationToken ct);

    Task RequestPauseAsync(Guid jobId, CancellationToken ct);
    Task RequestResumeAsync(Guid jobId, CancellationToken ct);
    Task RequestCancelAsync(Guid jobId, CancellationToken ct);
}
