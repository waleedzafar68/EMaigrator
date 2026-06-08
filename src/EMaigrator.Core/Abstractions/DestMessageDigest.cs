using EMaigrator.Core.Model;

namespace EMaigrator.Core.Abstractions;

/// <summary>Metadata-only snapshot of one destination message: its Message-ID, the provider's
/// internal id, and its attachment metadata (never bytes). Produced by IReconcilableDestination.ScanFolderAsync.</summary>
public sealed record DestMessageDigest(
    string InternetMessageId,
    string DestMessageId,
    IReadOnlyList<CanonicalAttachmentInfo> Attachments);
