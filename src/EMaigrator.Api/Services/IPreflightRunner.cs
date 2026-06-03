using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Api.Services;

/// <summary>
/// Runs the background pre-flight analysis for a job: builds the connectors, invokes the
/// <see cref="EMaigrator.Core.Preflight.IPreflightAnalyzer"/>, persists the plan to the API-owned side
/// store, flips the Job to AwaitingApproval, and pushes the SignalR status change.
/// </summary>
public interface IPreflightRunner
{
    Task RunAsync(Guid jobId, CancellationToken ct);
}
