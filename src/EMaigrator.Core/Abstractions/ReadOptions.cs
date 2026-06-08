namespace EMaigrator.Core.Abstractions;

/// <summary>Date-window + read options for reading messages (CONTRACTS.md §2).</summary>
public sealed record ReadOptions
{
    public DateTimeOffset? Since { get; init; }
    public DateTimeOffset? Before { get; init; }

    /// <summary>When true, the source parses MIME STRUCTURE to populate
    /// <see cref="EMaigrator.Core.Model.CanonicalMessage.Attachments"/>. Default false (the normal
    /// migrate path pays nothing). Set true by reconcile so the attachment diff has source metadata.</summary>
    public bool IncludeAttachmentMetadata { get; init; }
}
