using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Security;

/// <summary>
/// The auth-endpoint brute-force guard: a per-IP fixed-window limiter (10 requests / minute) applied to
/// <c>/auth/register</c> and <c>/auth/login</c>. The partition key is the connection's remote IP — the
/// authoritative, client-uncontrollable identity in production behind a trusted proxy. A client-supplied
/// <c>X-Client-Id</c> header is used ONLY as the fallback when there is no remote IP (the in-process test
/// host exposes a null <see cref="ConnectionInfo.RemoteIpAddress"/>), which lets each test client isolate
/// its own bucket. In production the header never takes precedence over the IP, so it cannot be used to
/// evade the limit.
/// </summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";

    /// <summary>The test-host fallback partition header (see the class remarks).</summary>
    public const string ClientIdHeader = "X-Client-Id";

    public static IServiceCollection AddEMaigratorRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Auth, context =>
            {
                ArgumentNullException.ThrowIfNull(context);

                var key = context.Connection.RemoteIpAddress?.ToString()
                          ?? context.Request.Headers[ClientIdHeader].ToString();
                if (string.IsNullOrEmpty(key))
                {
                    key = "unknown";
                }

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });
    }
}
