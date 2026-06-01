using EMaigrator.Core.Model;

namespace EMaigrator.Core.Diagnostics;

/// <summary>
/// Pure folder-depth flattener: collapses segments beyond <paramref name="maxDepth"/> into the
/// final kept segment joined by <paramref name="joinChar"/> (e.g. /A/B/C/D/E → A-B-C-D-E for a
/// 1-deep destination). (CONTRACTS.md §3, DESIGN.md §7)
/// </summary>
public static class FolderFlattener
{
    public static FolderPath Flatten(FolderPath path, int maxDepth, char joinChar = '-')
    {
        ArgumentNullException.ThrowIfNull(path);
        if (maxDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "maxDepth must be positive.");

        if (path.Depth <= maxDepth)
            return path;

        var kept = path.Segments.Take(maxDepth - 1).ToList();
        var tail = string.Join(joinChar, path.Segments.Skip(maxDepth - 1));
        kept.Add(tail);
        return new FolderPath(kept);
    }
}
