namespace EMaigrator.Connectors.Graph;

/// <summary>Flat Graph mailFolder projection used by <see cref="GraphFolderMapper"/>.</summary>
public sealed record GraphMailFolderNode(string Id, string DisplayName, string? ParentFolderId, long TotalItemCount);

/// <summary>Resolved well-known folder ids for the mailbox (from /mailFolders/{wellKnownName}).</summary>
public sealed record GraphFolderWellKnown(string? InboxId, string? DraftsId, string? SentItemsId, string? DeletedItemsId);
