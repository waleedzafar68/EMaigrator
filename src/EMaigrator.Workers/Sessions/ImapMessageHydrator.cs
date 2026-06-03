using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Sessions;

/// <summary>Re-enumerate the folder and materialize the single message whose IdentityKey == reference.</summary>
public sealed class ImapMessageHydrator : IMessageHydrator
{
    public async Task<CanonicalMessage> HydrateAsync(
        ISourceProvider source, FolderPath folder, string reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        await foreach (var m in source.ReadMessagesAsync(folder, new ReadOptions(), ct).ConfigureAwait(false))
        {
            if (string.Equals(m.IdentityKey, reference, StringComparison.Ordinal))
            {
                return m;
            }
        }

        throw new InvalidOperationException($"No message with identity '{reference}' in folder '{folder}'.");
    }
}
