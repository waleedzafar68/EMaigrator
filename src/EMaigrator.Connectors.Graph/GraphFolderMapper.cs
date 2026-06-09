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

        // Route common source special-folder names onto the mailbox's well-known folders, so e.g. Gmail's
        // "SENT" or IMAP's "Sent" lands in Exchange's "Sent Items" instead of a stray literal folder.
        // These aliases OVERWRITE a same-named DisplayName path on purpose: if a prior failed run created a
        // literal "SENT" folder, the alias must still win so mail routes to the real Sent Items.
        AddWellKnownAliases(index, wellKnown);

        return index;
    }

    private static void AddWellKnownAliases(Dictionary<string, string> index, GraphFolderWellKnown wellKnown)
    {
        void Alias(string? id, params string[] names)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            foreach (var name in names)
            {
                index[name] = id;
            }
        }

        // Cover both Gmail's UPPERCASE system labels and IMAP/standard mixed-case names.
        Alias(wellKnown.InboxId, "INBOX", "Inbox");
        Alias(wellKnown.SentItemsId, "SENT", "Sent", "Sent Items", "Sent Mail", "[Gmail]/Sent Mail");
        Alias(wellKnown.DraftsId, "DRAFT", "DRAFTS", "Drafts", "[Gmail]/Drafts");
        Alias(wellKnown.JunkEmailId, "SPAM", "Junk", "Junk Email", "Junk E-Mail", "[Gmail]/Spam");
        Alias(wellKnown.DeletedItemsId, "TRASH", "Trash", "Deleted Items", "Bin", "Deleted", "[Gmail]/Trash");
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
