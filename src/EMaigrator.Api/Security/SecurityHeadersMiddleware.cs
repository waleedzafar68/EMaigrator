using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace EMaigrator.Api.Security;

/// <summary>
/// Stamps hardening response headers on every response: <c>X-Content-Type-Options: nosniff</c>,
/// <c>X-Frame-Options: DENY</c>, <c>Referrer-Policy: no-referrer</c>, and a restrictive
/// <c>Content-Security-Policy</c>. HTTPS responses additionally carry HSTS. Registered first in the
/// pipeline so the headers ride on every response — including errors and short-circuited requests.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] =
            "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'";

        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        await _next(context).ConfigureAwait(false);
    }
}
