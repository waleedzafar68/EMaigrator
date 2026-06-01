using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Infrastructure.Messaging;

/// <summary>
/// MassTransit-backed orchestrator. Enqueue publishes <see cref="StartMigration"/>; pause/resume/cancel
/// publish their control contracts (<see cref="PauseJob"/>/<see cref="ResumeJob"/>/<see cref="CancelJob"/>).
/// Workers consume these (Plan 07). Kept behind <see cref="IJobOrchestrator"/> so the transport
/// (RabbitMQ today) is swappable.
/// </summary>
public sealed class MassTransitJobOrchestrator : IJobOrchestrator
{
    private readonly IPublishEndpoint _publish;
    private readonly IBus _bus;

    public MassTransitJobOrchestrator(IPublishEndpoint publish, IBus bus)
    {
        _publish = publish;
        _bus = bus;
    }

    public Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct) =>
        _publish.Publish(new StartMigration(mailboxMigrationId), ct);

    public Task RequestPauseAsync(Guid jobId, CancellationToken ct) =>
        _publish.Publish(new PauseJob(jobId), ct);

    public Task RequestResumeAsync(Guid jobId, CancellationToken ct) =>
        _publish.Publish(new ResumeJob(jobId), ct);

    public Task RequestCancelAsync(Guid jobId, CancellationToken ct) =>
        _publish.Publish(new CancelJob(jobId), ct);
}
