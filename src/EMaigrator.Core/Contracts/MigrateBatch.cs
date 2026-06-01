namespace EMaigrator.Core.Contracts;

/// <summary>Command: migrate a small batch of messages within a folder (CONTRACTS.md §4).</summary>
public sealed record MigrateBatch(Guid MailboxMigrationId, Guid FolderTaskId, string SourceFolder,
    string DestFolder, IReadOnlyList<string> SourceMessageRefs);
