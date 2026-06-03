using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EMaigrator.Workers.IntegrationTests.Security;

/// <summary>
/// Snapshots the set of files under the process's temp + working directories before a pipeline run,
/// then (after the run) reports any NEW file whose text content contains a given sentinel. Used to
/// prove the streaming copier never spills a message body to disk.
/// </summary>
public sealed class TempDirWatcher
{
    private readonly IReadOnlyList<string> _roots;
    private readonly HashSet<string> _baseline;

    private TempDirWatcher(IReadOnlyList<string> roots, HashSet<string> baseline)
    {
        _roots = roots;
        _baseline = baseline;
    }

    /// <summary>Captures the current file set across temp + working dirs as the "before" baseline.</summary>
    public static TempDirWatcher Snapshot()
    {
        var roots = new[]
        {
            Path.GetTempPath(),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        }
        .Where(r => !string.IsNullOrWhiteSpace(r))
        .Select(NormalizeRoot)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var baseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var file in EnumerateFilesSafe(root))
            {
                baseline.Add(file);
            }
        }

        return new TempDirWatcher(roots, baseline);
    }

    /// <summary>
    /// Returns every NEW file (not present at snapshot time) under the watched roots whose text
    /// content contains <paramref name="sentinel"/>. Locked/binary/unreadable files are skipped.
    /// </summary>
    public IReadOnlyList<string> NewFilesContaining(string sentinel)
    {
        var hits = new List<string>();
        foreach (var root in _roots)
        {
            foreach (var file in EnumerateFilesSafe(root))
            {
                if (_baseline.Contains(file))
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (System.Security.SecurityException)
                {
                    continue;
                }

                if (text.Contains(sentinel, StringComparison.Ordinal))
                {
                    hits.Add(file);
                }
            }
        }

        return hits;
    }

    private static string NormalizeRoot(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        try
        {
            // EnumerationOptions with IgnoreInaccessible swallows locked subtrees during enumeration.
            return Directory.EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
