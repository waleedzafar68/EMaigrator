using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Declared Microsoft 365 / Exchange Online mailbox constraints used by pre-flight
/// (DESIGN.md §7). Depth limit reflects Exchange Online's documented folder-hierarchy
/// limit; the 150 MB cap reflects the maximum message size for Exchange Online.
/// </summary>
public static class GraphConstraints
{
    private const long Mb = 1024 * 1024;

    public static readonly ProviderConstraints MS365 = new()
    {
        MaxFolderDepth = 300,
        MaxPathLengthChars = 16_000,
        IllegalNameChars = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' },
        MaxMessageBytes = 150L * Mb,
        MaxAttachmentBytes = 150L * Mb,
        FolderSeparator = '/',
        ReservedFolderNames = new[]
        {
            "Inbox", "Sent Items", "Drafts", "Deleted Items",
            "Junk Email", "Archive", "Outbox",
        },
    };
}
