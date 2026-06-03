using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Api.Services;

/// <summary>
/// A minimal in-process background work queue. The preflight POST enqueues a unit of work that the
/// <see cref="QueuedHostedService"/> drains on a background thread, creating a fresh DI scope per item.
/// Tests swap this abstraction for an inline implementation so the work runs synchronously.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "This is a genuine work queue; 'Queue' is the accurate, intended suffix for the abstraction.")]
public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem);
}
