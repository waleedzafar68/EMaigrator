namespace EMaigrator.Core.Contracts;

/// <summary>Command: begin a mailbox migration (CONTRACTS.md §4).</summary>
public sealed record StartMigration(Guid MailboxMigrationId);
