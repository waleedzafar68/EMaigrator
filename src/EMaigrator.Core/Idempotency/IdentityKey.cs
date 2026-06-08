using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EMaigrator.Core.Idempotency;

/// <summary>
/// Computes the idempotency identity key. Primary: normalized Message-ID. Fallback: composite
/// SHA-256 hex over normalized From|To|Subject|Date|&lt;decoded-body-sha256&gt;. NEVER hashes raw
/// transport bytes (servers rewrite messages in transit). The hash is a content fingerprint, not
/// a security control. (CONTRACTS.md §1, DESIGN.md §6)
/// </summary>
public static class IdentityKey
{
    public static string Compute(MessageIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var normalizedId = NormalizeMessageId(input.MessageId);
        if (normalizedId is not null)
            return "mid:" + normalizedId;

        var canonical = string.Join('|',
            NormalizeAddress(input.From),
            NormalizeAddress(input.To),
            NormalizeText(input.Subject),
            NormalizeDate(input.Date),
            NormalizeText(input.DecodedBodySha256Hex));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "h:" + Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Normalizes an RFC Message-ID for matching/indexing: trims, lowercases, and strips exactly one
    /// surrounding pair of angle brackets. Returns null for null/empty/whitespace. Used by both
    /// <see cref="Compute"/> and reconcile's source↔destination index (so both sides key identically).
    /// </summary>
    public static string? NormalizeMessageId(string? messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return null;
        var trimmed = messageId.Trim().ToLowerInvariant();
        // Strip exactly one surrounding pair of angle brackets.
        if (trimmed.Length >= 2 && trimmed[0] == '<' && trimmed[^1] == '>')
            trimmed = trimmed[1..^1].Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeAddress(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static string NormalizeDate(DateTimeOffset? date)
        => date is null ? "" : date.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
