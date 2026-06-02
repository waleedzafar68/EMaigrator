using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Control;

/// <summary>
/// Distributed pause/cancel gate. Every stateless worker consults this before pulling new work
/// for a job, so Pause/Cancel take effect uniformly across the worker pool while in-flight
/// batches drain. State lives in Redis (the shared backplane).
/// </summary>
public interface IMigrationControlGate
{
    Task<MigrationControlState> GetStateAsync(Guid jobId, CancellationToken ct);
    Task PauseAsync(Guid jobId, CancellationToken ct);
    Task ResumeAsync(Guid jobId, CancellationToken ct);
    Task CancelAsync(Guid jobId, CancellationToken ct);
}
