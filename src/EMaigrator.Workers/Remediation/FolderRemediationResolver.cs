using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Workers.Remediation;

/// <summary>
/// Pure: maps a source folder to its destination folder by applying the approved structural
/// remediation. No silent defaults — only explicitly-approved actions transform the path
/// (DESIGN.md §7 "no silent defaults").
/// </summary>
public static class FolderRemediationResolver
{
    public static FolderPath Resolve(
        FolderPath source,
        IReadOnlyList<ApprovedRemediation> approved,
        ProviderConstraints destConstraints)
    {
        var sourceKey = source.ToString();
        RemediationAction action = RemediationAction.None;
        foreach (var r in approved)
        {
            if (string.Equals(r.SourceFolder, sourceKey, System.StringComparison.Ordinal))
            {
                action = r.Action;
                break;
            }
        }

        return action switch
        {
            RemediationAction.FlattenFolder => FolderFlattener.Flatten(source, destConstraints.MaxFolderDepth),
            RemediationAction.SanitizeFolderName => FolderSanitizer.Sanitize(source, destConstraints),
            _ => source
        };
    }
}
