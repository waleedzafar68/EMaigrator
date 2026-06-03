using System;

namespace EMaigrator.Api.Data;

/// <summary>
/// API-owned idempotency marker for terminal-state notifications. Presence of a row (keyed on the
/// mailbox-migration id) means the migration was already notified. Lives in <see cref="ApiSideContext"/>
/// (NOT the frozen CONTRACTS §5 schema), so the engine's <c>MailboxMigration</c> shape is never altered.
/// </summary>
public sealed class NotificationSentRow
{
    /// <summary>Primary key — the mailbox-migration id; presence = already notified.</summary>
    public Guid MailboxMigrationId { get; set; }

    public DateTimeOffset SentAt { get; set; }
}
