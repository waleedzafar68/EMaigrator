namespace EMaigrator.Core.Diagnostics;

/// <summary>Concrete remediation actions (CONTRACTS.md §3).</summary>
public enum RemediationAction
{
    None,
    RetryWithBackoff,
    FlattenFolder,
    SanitizeFolderName,
    RenameFolder,
    MergeFolder,
    SkipMessage,
}
