namespace EMaigrator.Infrastructure.Data;

public enum MailboxMigrationStatus
{
    Pending,
    Running,
    Completed,
    Partial,
    Failed,
    Cancelled,
}
