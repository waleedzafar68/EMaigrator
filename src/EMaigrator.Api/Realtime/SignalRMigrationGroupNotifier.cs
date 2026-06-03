using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace EMaigrator.Api.Realtime;

/// <summary>
/// Pushes migration events to the per-migration SignalR group via the strongly-typed hub context.
/// With the Redis backplane enabled (production) the push fans out across every API node; in tests the
/// backplane is off and delivery is in-process.
/// </summary>
public sealed class SignalRMigrationGroupNotifier : IMigrationGroupNotifier
{
    private readonly IHubContext<MigrationsHub, IMigrationProgressClient> _hub;

    public SignalRMigrationGroupNotifier(IHubContext<MigrationsHub, IMigrationProgressClient> hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    public Task PushProgressAsync(MigrationProgressDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return _hub.Clients.Group(dto.MigrationId).Progress(dto);
    }

    public Task PushStatusChangedAsync(string migrationId, string status) =>
        _hub.Clients.Group(migrationId).StatusChanged(migrationId, status);

    public Task PushNeedsDecisionAsync(string migrationId, NeedsDecisionDto dto) =>
        _hub.Clients.Group(migrationId).NeedsDecision(migrationId, dto);
}
