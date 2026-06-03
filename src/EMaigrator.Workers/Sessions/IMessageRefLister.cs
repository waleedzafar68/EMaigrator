using System.Collections.Generic;
using System.Threading;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Sessions;

/// <summary>
/// Enumerates opaque source message references for a folder (UIDs / Graph ids / Gmail ids).
/// Batches carry refs — never bodies — so a queued batch holds no message content.
/// </summary>
public interface IMessageRefLister
{
    IAsyncEnumerable<string> ListRefsAsync(ISourceProvider source, FolderPath folder, CancellationToken ct);
}
