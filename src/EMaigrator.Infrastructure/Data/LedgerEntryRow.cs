using EMaigrator.Core.Abstractions;

namespace EMaigrator.Infrastructure.Data;

/// <summary>
/// Idempotency ledger row. UNIQUE(MailboxMigrationId, IdentityKey).
/// NEVER stores message body, attachment, or subject. Identity hashes + folder mapping + status only.
/// </summary>
public class LedgerEntryRow
{
    public long Id { get; set; }
    public Guid MailboxMigrationId { get; set; }
    public string IdentityKey { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public string DestFolder { get; set; } = "";
    public LedgerStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
