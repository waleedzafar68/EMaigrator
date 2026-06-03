using System.Threading.Tasks;

namespace EMaigrator.Api.Realtime;

/// <summary>Server → client SignalR contract (CONTRACTS §6).</summary>
public interface IMigrationProgressClient
{
    Task Progress(MigrationProgressDto dto);

    Task StatusChanged(string migrationId, string status);

    Task NeedsDecision(string migrationId, NeedsDecisionDto dto);
}
