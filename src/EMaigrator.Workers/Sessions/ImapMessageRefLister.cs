using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Sessions;

/// <summary>Refs ARE identity keys: enumerate the source folder and yield each message's IdentityKey.</summary>
public sealed class ImapMessageRefLister : IMessageRefLister
{
    public async IAsyncEnumerable<string> ListRefsAsync(
        ISourceProvider source, FolderPath folder, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        await foreach (var m in source.ReadMessagesAsync(folder, new ReadOptions(), ct).ConfigureAwait(false))
        {
            yield return m.IdentityKey;
        }
    }
}
