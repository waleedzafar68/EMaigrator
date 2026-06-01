using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Diagnostics;

/// <summary>
/// Pure folder-name sanitizer: replaces illegal chars, suffixes reserved names, and truncates
/// to the path-length limit per the destination's <see cref="ProviderConstraints"/>. (CONTRACTS.md §3)
/// </summary>
public static class FolderSanitizer
{
    public static FolderPath Sanitize(FolderPath path, ProviderConstraints c)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(c);

        var illegal = c.IllegalNameChars.ToHashSet();
        var reserved = c.ReservedFolderNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var segments = new List<string>(path.Segments.Count);
        foreach (var raw in path.Segments)
        {
            var chars = raw.Select(ch => illegal.Contains(ch) ? '_' : ch).ToArray();
            var seg = new string(chars).Trim();
            if (reserved.Contains(seg))
                seg += "_";
            segments.Add(seg);
        }

        TruncateToPathLength(segments, c.FolderSeparator, c.MaxPathLengthChars);
        return new FolderPath(segments);
    }

    private static void TruncateToPathLength(List<string> segments, char separator, int maxLen)
    {
        if (maxLen == int.MaxValue || segments.Count == 0)
            return;

        var total = segments.Sum(s => s.Length) + (segments.Count - 1); // separators
        if (total <= maxLen)
            return;

        var overflow = total - maxLen;
        var last = segments[^1];
        var keep = Math.Max(1, last.Length - overflow);
        segments[^1] = last[..keep];
    }
}
