using EMaigrator.Core.Model;

namespace EMaigrator.Core.Abstractions;

/// <summary>
/// OPTIONAL destination capability for reconcile/repair. Implemented only by destinations that can
/// (a) enumerate existing messages + their attachment metadata, and (b) add an attachment to an
/// EXISTING message (Graph/Exchange). Gmail/IMAP messages are immutable → they do NOT implement this.
/// Discovered via an `is IReconcilableDestination` cast on the IDestinationProvider.
/// </summary>
public interface IReconcilableDestination
{
    /// <summary>Page the destination folder once; yield a metadata-only digest per message (no bodies).</summary>
    IAsyncEnumerable<DestMessageDigest> ScanFolderAsync(FolderPath folder, CancellationToken ct);

    /// <summary>Backfill ONLY the given missing attachments onto an existing destination message. The
    /// implementation opens <paramref name="source"/>'s content, parses the MIME, extracts just the
    /// missing parts, and uploads them. Bytes transit memory only.</summary>
    Task<BackfillResult> BackfillAttachmentsAsync(
        FolderPath folder, string destMessageId, CanonicalMessage source,
        IReadOnlyList<CanonicalAttachmentInfo> missing, CancellationToken ct);
}
