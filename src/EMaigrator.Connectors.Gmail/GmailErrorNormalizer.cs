using System;
using System.Linq;
using Google;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Normalizes Gmail/Google API failures into a stable, credential-free error signature
/// of the form "gmail:&lt;status&gt;:&lt;reason&gt;" for catalog matching (CONTRACTS §8).
/// The signature deliberately omits the impersonated mailbox and SA identity so quota
/// errors never leak account identity to end users (DESIGN.md §10).
/// </summary>
public static class GmailErrorNormalizer
{
    public static string Normalize(Exception ex)
    {
        if (ex is not GoogleApiException gex)
            return "gmail:unknown";

        var status = gex.HttpStatusCode == 0
            ? "unknown"
            : ((int)gex.HttpStatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var reason = gex.Error?.Errors?.FirstOrDefault()?.Reason;

        if (string.IsNullOrWhiteSpace(reason))
            reason = status switch
            {
                "401" => "authError",
                "403" => "forbidden",
                "404" => "notFound",
                "429" => "rateLimitExceeded",
                _ => "error",
            };

        // Reason values come from a closed Google vocabulary (rateLimitExceeded, quotaExceeded,
        // userRateLimitExceeded, authError, notFound, ...) — they never contain PII. We still
        // strip anything past whitespace defensively so no free-text/email can ride along.
        reason = new string(reason.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());

        return $"gmail:{status}:{reason}";
    }

    /// <summary>
    /// Parses a raw HTTP <c>Retry-After</c> header value (delta-seconds form) into a
    /// <see cref="TimeSpan"/>. Google's typed exception does not expose response headers, so the
    /// provider passes the header it read off the HTTP response. Returns null for a null/empty/
    /// non-numeric value, and clamps negative deltas to null. The HTTP-date form is not used by
    /// Gmail quota responses and is intentionally not parsed here.
    /// </summary>
    public static TimeSpan? TryParseRetryAfter(string? retryAfterHeader)
    {
        if (string.IsNullOrWhiteSpace(retryAfterHeader))
            return null;

        if (!int.TryParse(retryAfterHeader.Trim(), out var seconds) || seconds < 0)
            return null;

        return TimeSpan.FromSeconds(seconds);
    }
}
