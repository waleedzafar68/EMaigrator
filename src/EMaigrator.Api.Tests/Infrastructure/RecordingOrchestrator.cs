using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Deterministic <see cref="IJobOrchestrator"/> test double for the run-control suite (Task 8). Records
/// every enqueue/pause/resume/cancel so a test can assert the approve/control endpoints drove the
/// orchestrator. Registered as a <b>singleton</b> (see <see cref="AddRecordingOrchestrator"/>) so the
/// instance resolved from the root provider (<c>_factory.Services</c>) is the same one the per-request
/// endpoint scope resolves.
/// </summary>
public sealed class RecordingOrchestrator : IJobOrchestrator
{
    public List<Guid> Enqueued { get; } = new();

    public List<Guid> Reconciled { get; } = new();

    public List<Guid> Paused { get; } = new();

    public List<Guid> Resumed { get; } = new();

    public List<Guid> Cancelled { get; } = new();

    public Task EnqueueMigrationAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        Enqueued.Add(mailboxMigrationId);
        return Task.CompletedTask;
    }

    public Task EnqueueReconcileAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        Reconciled.Add(mailboxMigrationId);
        return Task.CompletedTask;
    }

    public Task RequestPauseAsync(Guid jobId, CancellationToken ct)
    {
        Paused.Add(jobId);
        return Task.CompletedTask;
    }

    public Task RequestResumeAsync(Guid jobId, CancellationToken ct)
    {
        Resumed.Add(jobId);
        return Task.CompletedTask;
    }

    public Task RequestCancelAsync(Guid jobId, CancellationToken ct)
    {
        Cancelled.Add(jobId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Wires the <see cref="RecordingOrchestrator"/> into the test host. <see cref="WithRecordingOrchestrator"/>
/// is a call-site marker; <see cref="ApiTestFactory"/> ALWAYS calls <see cref="AddRecordingOrchestrator"/>.
/// </summary>
public static class RecordingOrchestratorExtensions
{
    public static ApiTestFactory WithRecordingOrchestrator(this ApiTestFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory;
    }

    /// <summary>
    /// REMOVE the production MassTransit-backed <see cref="IJobOrchestrator"/> (registered scoped by
    /// <c>AddInfrastructure</c>) then register the recorder as a singleton: <c>RemoveAll</c> guarantees the
    /// recorder is the only registration so it is the one the endpoint scope resolves, and the singleton
    /// lifetime lets the test read the SAME instance from the root provider (<c>_factory.Services</c>).
    /// </summary>
    public static void AddRecordingOrchestrator(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IJobOrchestrator>();
        services.AddSingleton<IJobOrchestrator, RecordingOrchestrator>();
    }
}
