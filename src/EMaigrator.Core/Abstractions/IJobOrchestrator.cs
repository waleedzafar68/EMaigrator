namespace EMaigrator.Core.Abstractions;

/// <summary>Queue/worker orchestration seam (MassTransit vs future Temporal) (CONTRACTS.md §4).</summary>
public interface IJobOrchestrator
{
    Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task RequestPauseAsync(Guid jobId, CancellationToken ct);
    Task RequestResumeAsync(Guid jobId, CancellationToken ct);
    Task RequestCancelAsync(Guid jobId, CancellationToken ct);
}
