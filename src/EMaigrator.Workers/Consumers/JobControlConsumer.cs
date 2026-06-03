using System;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Orchestration;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Applies job-level control. Pause/Cancel flip the distributed gate (workers drain).
/// Resume clears the gate and re-enqueues StartMigration for every not-done migration of the job.
/// </summary>
public sealed partial class JobControlConsumer :
    IConsumer<PauseJob>,
    IConsumer<ResumeJob>,
    IConsumer<CancelJob>
{
    private readonly IMigrationControlGate _gate;
    private readonly IJobMigrationLookup _lookup;
    private readonly ILogger<JobControlConsumer> _log;

    public JobControlConsumer(IMigrationControlGate gate, IJobMigrationLookup lookup, ILogger<JobControlConsumer> log)
    {
        _gate = gate;
        _lookup = lookup;
        _log = log;
    }

    public async Task Consume(ConsumeContext<PauseJob> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _gate.PauseAsync(context.Message.JobId, context.CancellationToken);
        LogPaused(context.Message.JobId);
    }

    public async Task Consume(ConsumeContext<CancelJob> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _gate.CancelAsync(context.Message.JobId, context.CancellationToken);
        LogCancelled(context.Message.JobId);
    }

    public async Task Consume(ConsumeContext<ResumeJob> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ct = context.CancellationToken;
        var jobId = context.Message.JobId;
        await _gate.ResumeAsync(jobId, ct);

        var migrations = await _lookup.GetNotDoneMigrationsAsync(jobId, ct);
        foreach (var mid in migrations)
            await context.Publish(new StartMigration(mid));

        LogResumed(jobId, migrations.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} paused.")]
    private partial void LogPaused(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} cancelled.")]
    private partial void LogCancelled(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} resumed; re-enqueued {Count} migrations.")]
    private partial void LogResumed(Guid jobId, int count);
}
