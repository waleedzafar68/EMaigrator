using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Provider constraints for Gmail. Gmail uses a flat-label model exposed to the
/// canonical engine as nested folders via the '/' separator; system labels are reserved.
/// </summary>
public static class GmailConstraints
{
    public static readonly ProviderConstraints Default = new()
    {
        MaxFolderDepth = int.MaxValue,
        MaxPathLengthChars = 225, // Gmail rejects label names longer than 225 chars
        IllegalNameChars = new[] { '/' },
        MaxMessageBytes = 35L * 1024 * 1024,    // ~35 MB total RFC822 size on import/insert
        MaxAttachmentBytes = 25L * 1024 * 1024, // 25 MB per-attachment limit
        FolderSeparator = '/',
        ReservedFolderNames = new[]
        {
            "INBOX", "SENT", "DRAFT", "DRAFTS", "SPAM", "TRASH",
            "STARRED", "IMPORTANT", "UNREAD", "CHAT",
            "CATEGORY_PERSONAL", "CATEGORY_SOCIAL", "CATEGORY_PROMOTIONS",
            "CATEGORY_UPDATES", "CATEGORY_FORUMS",
        },
    };
}
