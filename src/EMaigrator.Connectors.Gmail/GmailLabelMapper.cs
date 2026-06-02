using System.Collections.Generic;
using System.Linq;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Pure translation between Gmail label names and canonical <see cref="FolderPath"/>.
/// Gmail nests labels with '/', matching the canonical separator. System labels
/// (INBOX, SENT, etc.) are reserved; CHAT is not migratable as a folder; the synthetic
/// "[Gmail]/All Mail" path is treated specially so reads never double-copy.
/// </summary>
public static class GmailLabelMapper
{
    public const string AllMailPath = "[Gmail]/All Mail";

    private static readonly HashSet<string> SystemLabels = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "INBOX", "SENT", "DRAFT", "SPAM", "TRASH", "STARRED", "IMPORTANT", "UNREAD", "CHAT",
        "CATEGORY_PERSONAL", "CATEGORY_SOCIAL", "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES", "CATEGORY_FORUMS",
    };

    /// <summary>
    /// State/virtual labels that are never migratable as ordinary folders: CHAT (Hangouts/Chat
    /// history, not mail) and UNREAD (a read-state flag, surfaced via <see cref="MessageFlags"/>,
    /// never a folder).
    /// </summary>
    private static readonly HashSet<string> NonMappable = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "CHAT", "UNREAD",
    };

    public static bool IsSystemLabel(string labelName) => SystemLabels.Contains(labelName);

    public static bool IsMappableLabel(string labelName) => !NonMappable.Contains(labelName);

    public static bool IsAllMail(FolderPath path)
        => path.ToString() == AllMailPath;

    public static FolderPath LabelNameToFolderPath(string labelName)
    {
        var segments = labelName.Split('/').Where(s => s.Length > 0).ToList();
        return new FolderPath(segments);
    }

    public static string FolderPathToLabelName(FolderPath path)
        => string.Join('/', path.Segments);
}
