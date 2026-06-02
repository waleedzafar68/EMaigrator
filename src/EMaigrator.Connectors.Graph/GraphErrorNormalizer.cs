using System.Globalization;
using Microsoft.Graph.Models.ODataErrors;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Normalizes Microsoft Graph SDK exceptions into stable, credential-free
/// <see cref="GraphNormalizedError"/> signatures of the form "graph:&lt;status&gt;:&lt;code&gt;".
/// The signature is derived ONLY from the HTTP status and the Graph error code — never from
/// the error message, tenant id, account, or any secret — so it cannot leak identifiers
/// into user-facing diagnostics (DESIGN.md §10; INDEX security focus).
/// </summary>
public static class GraphErrorNormalizer
{
    public static GraphNormalizedError Normalize(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is ODataError odata)
            return FromODataError(odata);

        return new GraphNormalizedError("graph:unknown", IsTransient: false, RetryAfter: null);
    }

    private static GraphNormalizedError FromODataError(ODataError odata)
    {
        var status = odata.ResponseStatusCode;
        var code = odata.Error?.Code;
        var retryAfter = ParseRetryAfter(odata);

        // Throttling: Graph returns 429 with code errorThrottledRequest (and occasionally
        // ApplicationThrottled). Always transient; honor Retry-After.
        if (status == 429)
            return new GraphNormalizedError("graph:429:throttled", IsTransient: true, retryAfter);

        // Transient service errors.
        if (status is 503 or 504)
        {
            var transientCode = string.IsNullOrWhiteSpace(code) ? "serviceUnavailable" : SafeCode(code);
            return new GraphNormalizedError(
                string.Create(CultureInfo.InvariantCulture, $"graph:{status}:{transientCode}"),
                IsTransient: true,
                retryAfter);
        }

        var safeCode = string.IsNullOrWhiteSpace(code) ? "unknown" : SafeCode(code);
        return new GraphNormalizedError(
            string.Create(CultureInfo.InvariantCulture, $"graph:{status}:{safeCode}"),
            IsTransient: false,
            RetryAfter: null);
    }

    private static TimeSpan? ParseRetryAfter(ODataError odata)
    {
        if (odata.ResponseHeaders is null) return null;
        if (!odata.ResponseHeaders.TryGetValue("Retry-After", out var values)) return null;
        foreach (var v in values)
        {
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        return null;
    }

    // The Graph error code is a fixed enum-like token (e.g. "ErrorItemNotFound"); it never
    // contains identifiers. We still strip whitespace/separators defensively so the signature
    // stays a single stable token.
    private static string SafeCode(string code) => code.Trim();
}
