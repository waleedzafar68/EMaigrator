namespace EMaigrator.Connectors.Graph.Reconcile;

/// <summary>One attachment to add to an existing Graph message. Content opened lazily; never persisted.</summary>
internal sealed record GraphAttachmentContent(
    string Name, string ContentType, bool IsInline, string? ContentId, long Size,
    Func<CancellationToken, Stream> OpenContent);
