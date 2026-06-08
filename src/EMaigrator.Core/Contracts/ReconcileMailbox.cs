namespace EMaigrator.Core.Contracts;

/// <summary>Command: reconcile one mailbox pair against the LIVE destination (CONTRACTS.md §4). Mirrors StartMigration.</summary>
public sealed record ReconcileMailbox(Guid MailboxMigrationId);
