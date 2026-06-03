using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Api.Tests.Security;

/// <summary>
/// Auth sweep: every non-public route under <c>/api/v1/migrations*</c> plus the SignalR hub negotiate
/// endpoint must reject an unauthenticated caller with 401. This proves the fallback authorization policy
/// (and the <c>[Authorize]</c> hub) covers the whole surface — no route silently opts out of auth.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RouteAuthSweepTests
{
    private readonly ApiInfraFixture _fx;
    private readonly ITestOutputHelper _out;

    public RouteAuthSweepTests(ApiInfraFixture fx, ITestOutputHelper o)
    {
        _fx = fx;
        _out = o;
    }

    public static IEnumerable<object[]> ProtectedRoutes() => new[]
    {
        new object[] { "GET",    "/api/v1/migrations" },
        new object[] { "POST",   "/api/v1/migrations" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001" },
        new object[] { "DELETE", "/api/v1/migrations/00000000-0000-0000-0000-000000000001" },
        new object[] { "PATCH",  "/api/v1/migrations/00000000-0000-0000-0000-000000000001/endpoints" },
        new object[] { "PUT",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/connection/from" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/connection/from/test" },
        new object[] { "PUT",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/scope" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/preflight" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/preflight" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/approve" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/pause" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/resume" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/cancel" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/results" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/audit" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/rerun" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/report" },
    };

    [Theory(Timeout = 30_000)]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Protected_route_returns_401_without_token(string method, string path)
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var req = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method is "POST" or "PUT" or "PATCH")
        {
            req.Content = JsonContent.Create(new { });
        }

        using var res = await client.SendAsync(req);
        _out.WriteLine($"{method,-6} {path} -> {(int)res.StatusCode}");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path} must require auth");
    }

    [Fact(Timeout = 30_000)]
    public async Task Hub_rejects_unauthenticated_connection()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        // SignalR negotiate without a token must be rejected (401).
        using var res = await client.PostAsync(
            new Uri("/hubs/migrations/negotiate?negotiateVersion=1", UriKind.Relative), null);
        _out.WriteLine($"HUB negotiate -> {(int)res.StatusCode}");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
