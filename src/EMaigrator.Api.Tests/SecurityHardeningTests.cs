using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Task 12 hardening: every response carries the security headers; CORS allows only the configured SPA
/// origin; the auth endpoints trip 429 once the per-window limit is exceeded.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityHardeningTests
{
    private readonly ApiInfraFixture _fx;

    public SecurityHardeningTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Security_headers_present_on_every_response()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var res = await client.GetAsync(new Uri("/health", UriKind.Relative));

        res.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
        res.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        res.Headers.Contains("X-Frame-Options").Should().BeTrue();
        res.Headers.Contains("Referrer-Policy").Should().BeTrue();
        res.Headers.Contains("Content-Security-Policy").Should().BeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task Cors_allows_configured_origin_only()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/v1/migrations", UriKind.Relative));
        allowed.Headers.Add("Origin", "http://localhost:5173");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        using var ok = await client.SendAsync(allowed);
        ok.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();

        using var denied = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/v1/migrations", UriKind.Relative));
        denied.Headers.Add("Origin", "https://evil.example.com");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        using var no = await client.SendAsync(denied);
        no.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact(Timeout = 30_000)]
    public async Task Auth_login_is_rate_limited()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        // Fixed bucket so this test trips 429 deterministically without contaminating other auth tests.
        client.DefaultRequestHeaders.Add("X-Client-Id", "ratelimit-task12");

        HttpResponseMessage? last = null;
        for (var i = 0; i < 25; i++)
        {
            last?.Dispose();
            last = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative),
                new { email = "x@y.z", password = "nope-nope-nope" });
        }

        // The fixed window (configured at 10/min) must trip 429 within 25 rapid attempts.
        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        last.Dispose();
    }
}
