namespace EMaigrator.Core.Model;

/// <summary>
/// A canonical message. NEVER holds its body in a field; content is opened as a stream on
/// demand (streaming pass-through — DESIGN.md §6/§10). The stream yields raw RFC822/MIME bytes.
/// (CONTRACTS.md §1)
/// </summary>
public sealed record CanonicalMessage
{
    /// <summary>Idempotency identity key (see IdentityKey). Always set by the source provider.</summary>
    public required string IdentityKey { get; init; }

    /// <summary>RFC Message-ID header value; may be null for the malformed long tail.</summary>
    public string? MessageId { get; init; }

    public required DateTimeOffset InternalDate { get; init; }
    public MessageFlags Flags { get; init; }

    /// <summary>Gmail labels / MS365 categories.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    public long SizeBytes { get; init; }
    public IReadOnlyList<CanonicalAttachmentInfo> Attachments { get; init; } = [];

    /// <summary>For logging only (toggleable); never required to perform a copy.</summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Opens the raw message content stream. Caller disposes. Bodies transit memory only.
    /// </summary>
    public required Func<CancellationToken, Task<Stream>> OpenContentAsync { get; init; }
}
