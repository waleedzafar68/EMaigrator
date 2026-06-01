namespace EMaigrator.Infrastructure.Data;

public class FolderTask
{
    public Guid Id { get; set; }
    public Guid MailboxMigrationId { get; set; }
    public string SourceFolder { get; set; } = "";
    public string DestFolder { get; set; } = "";
    public string Status { get; set; } = "";
}
