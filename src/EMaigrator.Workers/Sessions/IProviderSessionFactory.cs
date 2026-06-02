using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Workers.Sessions;

public interface IProviderSessionFactory
{
    Task<ISourceProvider> CreateSourceAsync(Guid mailboxMigrationId, CancellationToken ct);
    Task<IDestinationProvider> CreateDestinationAsync(Guid mailboxMigrationId, CancellationToken ct);
}
