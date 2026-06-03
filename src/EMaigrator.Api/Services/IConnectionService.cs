using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Api.Services;

/// <summary>
/// Stores and tests one side's connection for a migration. Storing persists the non-secret settings on
/// the Job and the secret via <c>ISecretStore</c> (returning a secretRef that is never echoed); testing
/// builds the connector via the discovered <c>IProviderPlugin</c> and maps a provider failure through the
/// error catalog into a stable code.
/// </summary>
public interface IConnectionService
{
    Task StoreConnectionAsync(Guid jobId, string side, ConnectionRequest request, CancellationToken ct);

    Task<ConnectionTestResult> TestConnectionAsync(Guid jobId, string side, CancellationToken ct);
}
