namespace EMaigrator.Cli;

/// <summary>
/// Process exit codes. 0 = success; non-zero per failure class so headless
/// callers (cron, CI, shell scripts) can branch on the reason.
/// </summary>
public enum CliExitCode
{
    Success = 0,
    UsageError = 2,
    ConnectionFailed = 3,
    PreflightBlocked = 4,
    MigrationFailed = 5,
    MigrationPartial = 6,
    ConfigError = 7,
    Cancelled = 130,
}
