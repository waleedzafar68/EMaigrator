namespace EMaigrator.Workers.Copy;

public enum CopyOutcome
{
    Migrated,
    Skipped,
    Throttled,
    Failed
}
