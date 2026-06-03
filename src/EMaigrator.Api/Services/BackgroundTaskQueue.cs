using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Api.Services;

/// <summary>
/// The production unbounded-channel background queue. Registered as itself (consumed by
/// <see cref="QueuedHostedService"/>) and as <see cref="IBackgroundTaskQueue"/> (consumed by endpoints).
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "This is a genuine work queue; 'Queue' is the accurate, intended suffix for the type.")]
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>();

    public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _channel.Writer.WriteAsync(workItem);
    }

    public IAsyncEnumerable<Func<IServiceProvider, CancellationToken, Task>> Reader(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Hosted service that drains the queue, creating a DI scope per work item. It depends on the concrete
/// <see cref="BackgroundTaskQueue"/> (NOT the <see cref="IBackgroundTaskQueue"/> abstraction) so that
/// swapping the <see cref="IBackgroundTaskQueue"/> registration in tests (an inline queue) never breaks
/// this pump — it keeps draining the never-written production channel as a harmless no-op.
/// </summary>
public sealed class QueuedHostedService : BackgroundService
{
    private readonly BackgroundTaskQueue _queue;
    private readonly IServiceProvider _root;

    public QueuedHostedService(BackgroundTaskQueue queue, IServiceProvider root)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(root);
        _queue = queue;
        _root = root;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader(stoppingToken))
        {
            using var scope = _root.CreateScope();
#pragma warning disable CA1031 // A failed background work item must never crash the pump; it is observed via OTel.
            try
            {
                await work(scope.ServiceProvider, stoppingToken);
            }
            catch
            {
                // Logged via OTel; never crash the pump.
            }
#pragma warning restore CA1031
        }
    }
}
