using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Pure mapping between Graph mailFolders and the canonical folder model (CONTRACTS §1).
/// A folder's canonical <see cref="FolderPath"/> is the chain of DisplayName segments from a
/// root (a node with no parent, or whose parent is the mailbox root) down to the node.
/// </summary>
public static class GraphFolderMapper
{
    public static IReadOnlyList<CanonicalFolder> BuildTree(
        IReadOnlyList<GraphMailFolderNode> nodes, GraphFolderWellKnown wellKnown)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(wellKnown);

        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var result = new List<CanonicalFolder>();

        foreach (var node in nodes)
        {
            var segments = TryBuildSegments(node, byId);
            if (segments is null)
            {
                continue;   // orphan: unknown parent → skip
            }

            var path = new FolderPath(segments);
            var specialUse = SpecialUseFor(node.Id, wellKnown);
            result.Add(new CanonicalFolder(path, node.TotalItemCount, specialUse));
        }

        return result;
    }

    public static IReadOnlyDictionary<string, string> BuildIdIndex(
        IReadOnlyList<GraphMailFolderNode> nodes, GraphFolderWellKnown wellKnown)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(wellKnown);

        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var segments = TryBuildSegments(node, byId);
            if (segments is null)
            {
                continue;
            }

            index[new FolderPath(segments).ToString()] = node.Id;
        }

        return index;
    }

    public static string? ResolveFolderId(FolderPath path, IReadOnlyDictionary<string, string> idsByPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(idsByPath);

        return idsByPath.TryGetValue(path.ToString(), out var id) ? id : null;
    }

    private static List<string>? TryBuildSegments(
        GraphMailFolderNode node, Dictionary<string, GraphMailFolderNode> byId)
    {
        var segments = new List<string>();
        var current = node;
        var guard = 0;

        while (true)
        {
            segments.Insert(0, current.DisplayName);

            if (string.IsNullOrEmpty(current.ParentFolderId))
            {
                return segments;   // reached a root
            }

            if (!byId.TryGetValue(current.ParentFolderId, out var parent))
            {
                return null;        // orphan: parent not in the node set → skip
            }

            current = parent;
            if (++guard > 512)
            {
                return null;   // defensive: malformed cycle
            }
        }
    }

    private static MessageFlags? SpecialUseFor(string id, GraphFolderWellKnown wellKnown)
    {
        if (string.Equals(id, wellKnown.DraftsId, StringComparison.Ordinal))
        {
            return MessageFlags.Draft;
        }

        if (string.Equals(id, wellKnown.DeletedItemsId, StringComparison.Ordinal))
        {
            return MessageFlags.Deleted;
        }

        return null;
    }
}
