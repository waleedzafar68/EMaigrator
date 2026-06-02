using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Workers.Orchestration;

public sealed class MassTransitJobOrchestrator : IJobOrchestrator
{
    private readonly IPublishEndpoint _publish;

    public MassTransitJobOrchestrator(IPublishEndpoint publish) => _publish = publish;

    public Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct)
        => _publish.Publish(new StartMigration(mailboxMigrationId), ct);

    public Task RequestPauseAsync(Guid jobId, CancellationToken ct)
        => _publish.Publish(new PauseJob(jobId), ct);

    public Task RequestResumeAsync(Guid jobId, CancellationToken ct)
        => _publish.Publish(new ResumeJob(jobId), ct);

    public Task RequestCancelAsync(Guid jobId, CancellationToken ct)
        => _publish.Publish(new CancelJob(jobId), ct);
}
