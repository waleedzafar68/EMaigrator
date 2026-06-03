using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests.Security;

/// <summary>
/// Independent re-assertion of the Task 12 hardening (security headers, CORS lock-down, auth rate limit)
/// so the security gate stands on its own captured evidence rather than trusting Task 12's class.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HardeningReassertTests
{
    private readonly ApiInfraFixture _fx;

    public HardeningReassertTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Security_headers_present()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var res = await client.GetAsync(new Uri("/health", UriKind.Relative));
        res.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
        res.Headers.Contains("X-Frame-Options").Should().BeTrue();
        res.Headers.Contains("Referrer-Policy").Should().BeTrue();
        res.Headers.Contains("Content-Security-Policy").Should().BeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task Cors_rejects_unconfigured_origin()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var denied = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/v1/migrations", UriKind.Relative));
        denied.Headers.Add("Origin", "https://evil.example.com");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        using var res = await client.SendAsync(denied);
        res.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact(Timeout = 30_000)]
    public async Task Auth_login_trips_429()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "ratelimit-gate-task13");

        HttpResponseMessage? last = null;
        for (var i = 0; i < 25; i++)
        {
            last?.Dispose();
            last = await client.PostAsJsonAsync(
                new Uri("/api/v1/auth/login", UriKind.Relative), new { email = "x@y.z", password = "nope-nope-nope" });
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        last.Dispose();
    }
}
