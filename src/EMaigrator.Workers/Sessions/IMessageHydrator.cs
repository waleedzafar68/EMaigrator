using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Sessions;

/// <summary>Materializes one opaque source ref into a CanonicalMessage (whose body opens as a stream on demand).</summary>
public interface IMessageHydrator
{
    Task<CanonicalMessage> HydrateAsync(ISourceProvider source, FolderPath folder, string reference, CancellationToken ct);
}
