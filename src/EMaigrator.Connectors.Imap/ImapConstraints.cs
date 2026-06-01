using System.Collections.Generic;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Constraints the IMAP transport imposes (DESIGN.md §7 — used by pre-flight when
/// IMAP is the destination). IMAP itself imposes no hard depth/size limit; the
/// real ceilings belong to the concrete server, so defaults are permissive.
/// </summary>
public static class ImapConstraints
{
    public static ProviderConstraints Default(char separator = '/') => new()
    {
        MaxFolderDepth = int.MaxValue,
        MaxPathLengthChars = int.MaxValue,
        IllegalNameChars = BuildIllegalChars(separator),
        MaxMessageBytes = long.MaxValue,
        MaxAttachmentBytes = long.MaxValue,
        FolderSeparator = separator,
        ReservedFolderNames = new[] { "INBOX" },
    };

    // Return the concrete HashSet (CA1859: avoids an interface dispatch / hidden boxing);
    // it still satisfies the IReadOnlyCollection<char> init property on ProviderConstraints.
    private static HashSet<char> BuildIllegalChars(char separator)
        => new() { separator, '\0', '\r', '\n', '\t' };
}
