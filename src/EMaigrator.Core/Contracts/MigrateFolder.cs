namespace EMaigrator.Core.Contracts;

/// <summary>Command: migrate one folder within a mailbox (CONTRACTS.md §4).</summary>
public sealed record MigrateFolder(Guid MailboxMigrationId, Guid FolderTaskId, string SourceFolder, string DestFolder);
