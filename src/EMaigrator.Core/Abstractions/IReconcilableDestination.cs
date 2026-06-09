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
    /// <summary>Page the destination folder once; yield a metadata-only digest per message (no bodies).
    /// When <paramref name="since"/>/<paramref name="before"/> are set the scan is restricted to that
    /// received-date window so a date-scoped reconcile reads only the relevant slice of a large folder.</summary>
    IAsyncEnumerable<DestMessageDigest> ScanFolderAsync(
        FolderPath folder, DateTimeOffset? since, DateTimeOffset? before, CancellationToken ct);

    /// <summary>Backfill ONLY the given missing attachments onto an existing destination message. The
    /// implementation opens <paramref name="source"/>'s content, parses the MIME, extracts just the
    /// missing parts, and uploads them. Bytes transit memory only.</summary>
    Task<BackfillResult> BackfillAttachmentsAsync(
        FolderPath folder, string destMessageId, CanonicalMessage source,
        IReadOnlyList<CanonicalAttachmentInfo> missing, CancellationToken ct);
}
