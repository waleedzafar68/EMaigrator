using Microsoft.Graph.Models;

namespace EMaigrator.Connectors.Graph;

/// <summary>Flat Graph mailFolder projection used by <see cref="GraphFolderMapper"/>.</summary>
public sealed record GraphMailFolderNode(string Id, string DisplayName, string? ParentFolderId, long TotalItemCount)
{
    /// <summary>
    /// Projects a COMPLETE set of Graph mailFolders into flat nodes, nulling the parent of every
    /// top-level folder so <see cref="GraphFolderMapper"/> treats it as a canonical root. A folder is
    /// top-level when its parent is the mailbox root, which Graph represents three ways: an empty parent,
    /// the URL alias "msgfolderroot", OR — what live Graph actually returns — the root's real id. The root
    /// is never itself returned in the folder list, so a parent absent from the fetched set IS the root.
    /// Without this, live top-level custom folders look like orphans (unknown parent) and get dropped, so
    /// they cannot be resolved or written to. Requires the full folder set (both providers page completely).
    /// </summary>
    public static IReadOnlyList<GraphMailFolderNode> BuildFromGraph(IReadOnlyList<MailFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var knownIds = folders.Where(f => f.Id is not null)
            .Select(f => f.Id!).ToHashSet(StringComparer.Ordinal);

        var nodes = new List<GraphMailFolderNode>(folders.Count);
        foreach (var f in folders)
        {
            var parent = f.ParentFolderId;
            var isTopLevel = string.IsNullOrEmpty(parent)
                || string.Equals(parent, "msgfolderroot", StringComparison.Ordinal)
                || !knownIds.Contains(parent);
            nodes.Add(new GraphMailFolderNode(
                f.Id!, f.DisplayName ?? "(unnamed)", isTopLevel ? null : parent, f.TotalItemCount ?? 0));
        }

        return nodes;
    }
}

/// <summary>Resolved well-known folder ids for the mailbox (from /mailFolders/{wellKnownName}).</summary>
public sealed record GraphFolderWellKnown(
    string? InboxId, string? DraftsId, string? SentItemsId, string? DeletedItemsId, string? JunkEmailId = null);
