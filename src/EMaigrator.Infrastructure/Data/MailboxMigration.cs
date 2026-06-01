namespace EMaigrator.Infrastructure.Data;

public class MailboxMigration
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string SourceMailbox { get; set; } = "";
    public string DestMailbox { get; set; } = "";
    public MailboxMigrationStatus Status { get; set; }
    public long MigratedCount { get; set; }
    public long SkippedCount { get; set; }
    public long FailedCount { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
