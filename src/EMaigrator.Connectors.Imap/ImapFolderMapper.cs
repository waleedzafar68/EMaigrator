using System;
using System.Collections.Generic;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Translates between an IMAP server's hierarchical full-name (which uses the
/// server-reported delimiter) and the canonical '/'-joined <see cref="FolderPath"/>.
/// </summary>
public static class ImapFolderMapper
{
    public static FolderPath ToFolderPath(string serverFullName, char delimiter)
    {
        if (string.IsNullOrEmpty(serverFullName))
            return new FolderPath(Array.Empty<string>());

        var segments = serverFullName.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        return new FolderPath(segments);
    }

    public static string ToServerName(FolderPath path, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsRoot)
            return string.Empty;
        return string.Join(delimiter, (IEnumerable<string>)path.Segments);
    }
}
