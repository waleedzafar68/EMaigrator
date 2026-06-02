using System.Collections.Generic;
using System.Linq;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Maps a Gmail message's label-id set into canonical <see cref="MessageFlags"/> and the
/// canonical user-label list. Gmail models read-state as the *absence* of the UNREAD label.
/// </summary>
public static class GmailFlagMapper
{
    public static MessageFlags ToFlags(IReadOnlyCollection<string> labelIds)
    {
        var set = new HashSet<string>(labelIds, System.StringComparer.OrdinalIgnoreCase);
        var flags = MessageFlags.None;

        // Read-state: UNREAD present => not seen; absent => seen.
        if (!set.Contains("UNREAD"))
            flags |= MessageFlags.Seen;

        if (set.Contains("STARRED"))
            flags |= MessageFlags.Flagged;

        if (set.Contains("DRAFT"))
            flags |= MessageFlags.Draft;

        return flags;
    }

    /// <summary>
    /// Returns the human-readable names of user labels only (system labels excluded),
    /// resolving id->name via the provided label map.
    /// </summary>
    public static IReadOnlyList<string> ToCanonicalLabels(
        IReadOnlyCollection<string> labelIds,
        IReadOnlyDictionary<string, string> labelIdToName)
    {
        return labelIds
            .Where(id => !GmailLabelMapper.IsSystemLabel(LookupName(id, labelIdToName)))
            .Select(id => LookupName(id, labelIdToName))
            .Where(name => name.Length > 0)
            .ToList();
    }

    private static string LookupName(string id, IReadOnlyDictionary<string, string> map)
        => map.TryGetValue(id, out var name) ? name : id;
}
