using System.Threading.Tasks;

namespace EMaigrator.Api.Realtime;

/// <summary>Fans migration events out to the per-migration SignalR group.</summary>
public interface IMigrationGroupNotifier
{
    Task PushProgressAsync(MigrationProgressDto dto);

    Task PushStatusChangedAsync(string migrationId, string status);

    Task PushNeedsDecisionAsync(string migrationId, NeedsDecisionDto dto);
}
