using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Preflight;

/// <summary>
/// Pure-logic pre-flight analyzer. Reads the source folder tree (the source provider performs the
/// only I/O), evaluates each scoped folder against the destination's <see cref="ProviderConstraints"/>,
/// and produces a remediation plan plus a migration estimate. (CONTRACTS.md §3, DESIGN.md §7/§14)
/// </summary>
public sealed class PreflightAnalyzer : IPreflightAnalyzer
{
    // Heuristics: tune later via real WorkMail data. Kept deterministic for unit testing.
    private const long AverageMessageBytes = 75_000;          // ~75 KB average message
    private const double MessagesPerMinuteThroughput = 600.0; // 10 msg/s sustained estimate

    public async Task<PreflightPlan> AnalyzeAsync(
        ISourceProvider source, IDestinationProvider dest, ScopeSpec scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);
        ArgumentNullException.ThrowIfNull(scope);
        ct.ThrowIfCancellationRequested();

        var allFolders = await source.ListFoldersAsync(ct);
        var scoped = ApplyScope(allFolders, scope);
        var constraints = dest.Constraints;

        var issues = new List<PreflightIssue>();
        foreach (var folder in scoped)
        {
            ct.ThrowIfCancellationRequested();
            var path = folder.Path;
            var pathString = path.ToString(constraints.FolderSeparator);

            if (path.Depth > constraints.MaxFolderDepth)
                issues.Add(new PreflightIssue(
                    "FolderTooDeep", new[] { path.ToString() },
                    RemediationAction.FlattenFolder,
                    new[] { RemediationAction.FlattenFolder, RemediationAction.RenameFolder },
                    Severity.Warning,
                    $"Folder depth {path.Depth} exceeds destination maximum of {constraints.MaxFolderDepth}."));

            if (HasIllegalChar(path, constraints.IllegalNameChars))
                issues.Add(new PreflightIssue(
                    "IllegalFolderName", new[] { path.ToString() },
                    RemediationAction.SanitizeFolderName,
                    new[] { RemediationAction.SanitizeFolderName, RemediationAction.RenameFolder },
                    Severity.Warning,
                    "Folder name contains characters the destination does not allow."));

            if (pathString.Length > constraints.MaxPathLengthChars)
                issues.Add(new PreflightIssue(
                    "FolderPathTooLong", new[] { path.ToString() },
                    RemediationAction.RenameFolder,
                    new[] { RemediationAction.RenameFolder, RemediationAction.FlattenFolder },
                    Severity.Warning,
                    $"Folder path length {pathString.Length} exceeds destination maximum of {constraints.MaxPathLengthChars}."));
        }

        var estimate = BuildEstimate(scoped, scope);
        return new PreflightPlan(issues, estimate);
    }

    private static List<CanonicalFolder> ApplyScope(IReadOnlyList<CanonicalFolder> folders, ScopeSpec scope)
    {
        IEnumerable<CanonicalFolder> q = folders;

        if (scope.IncludeFolders is { Count: > 0 } include)
        {
            var set = include.ToHashSet(StringComparer.OrdinalIgnoreCase);
            q = q.Where(f => set.Contains(f.Path.ToString()));
        }
        if (scope.ExcludeFolders is { Count: > 0 } exclude)
        {
            var set = exclude.ToHashSet(StringComparer.OrdinalIgnoreCase);
            q = q.Where(f => !set.Contains(f.Path.ToString()));
        }
        return q.ToList();
    }

    private static bool HasIllegalChar(FolderPath path, IReadOnlyCollection<char> illegal)
    {
        if (illegal.Count == 0)
            return false;
        var set = illegal.ToHashSet();
        return path.Segments.Any(seg => seg.Any(set.Contains));
    }

    private static MigrationEstimate BuildEstimate(List<CanonicalFolder> scoped, ScopeSpec scope)
    {
        var mailboxCount = Math.Max(1, scope.Pairs.Count);
        var folderCount = scoped.Count;
        var messageCount = scoped.Sum(f => f.EstimatedMessageCount);
        var totalBytes = messageCount * AverageMessageBytes;
        var minutes = messageCount / MessagesPerMinuteThroughput;
        var duration = TimeSpan.FromMinutes(minutes);
        return new MigrationEstimate(mailboxCount, folderCount, messageCount, totalBytes, duration);
    }
}
