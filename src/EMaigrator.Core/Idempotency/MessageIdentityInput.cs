namespace EMaigrator.Core.Idempotency;

/// <summary>
/// Inputs for <see cref="IdentityKey.Compute"/>. The body is represented ONLY by its decoded-body
/// SHA-256 — the caller computes that over decoded body text, never over raw transport bytes
/// (DESIGN.md §6). (CONTRACTS.md §1)
/// </summary>
public sealed record MessageIdentityInput
{
    public string? MessageId { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Subject { get; init; }
    public DateTimeOffset? Date { get; init; }
    public required string DecodedBodySha256Hex { get; init; }
}
