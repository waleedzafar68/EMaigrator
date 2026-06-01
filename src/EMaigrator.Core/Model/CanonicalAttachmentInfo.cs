namespace EMaigrator.Core.Model;

/// <summary>Attachment metadata only — never the bytes (CONTRACTS.md §1).</summary>
public sealed record CanonicalAttachmentInfo(string FileName, string ContentType, long SizeBytes);
