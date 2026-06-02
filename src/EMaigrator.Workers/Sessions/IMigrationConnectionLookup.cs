using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Workers.Sessions;

/// <summary>Resolves a mailbox migration's job, tenant and source/dest connection descriptors (Infrastructure-backed).</summary>
public interface IMigrationConnectionLookup
{
    Task<MigrationConnections> GetAsync(Guid mailboxMigrationId, CancellationToken ct);
}
