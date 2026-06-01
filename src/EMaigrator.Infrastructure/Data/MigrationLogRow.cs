namespace EMaigrator.Infrastructure.Data;

/// <summary>
/// Migration audit log. Encrypted at rest; 30-day purge. Subject is nullable and omitted when
/// Job.StoreSubjects == false. NO sender/recipient.
/// </summary>
public class MigrationLogRow
{
    public long Id { get; set; }
    public Guid MailboxMigrationId { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset MessageDate { get; set; }
    public string SourceFolder { get; set; } = "";
    public string DestFolder { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
