using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Exercises <c>POST /api/v1/auth/logout</c>: it returns 204 and clears the <c>emaigrator.auth</c>
/// cookie (a Set-Cookie carrying an empty value + a past expiry), and it works anonymously — clearing
/// the cookie must not require a still-valid session.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LogoutEndpointTests
{
    private const string AuthCookieName = "emaigrator.auth";

    private readonly ApiInfraFixture _fx;

    public LogoutEndpointTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Logout_returns_204_and_clears_the_auth_cookie()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var res = await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null);

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        res.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue(
            "logout must emit a Set-Cookie that clears the auth cookie");
        var clear = cookies!.Single(c => c.StartsWith($"{AuthCookieName}=", StringComparison.Ordinal));

        // An expired (cleared) cookie carries an empty value and a past expiry.
        clear.Should().StartWith($"{AuthCookieName}=;", "the cleared cookie has an empty value");
        clear.Should().Contain("expires=Thu, 01 Jan 1970", "the cleared cookie expires in the past");
    }

    [Fact(Timeout = 30_000)]
    public async Task Logout_is_anonymous_even_without_a_session()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        // No Authorization header / no cookie: logout must still succeed (204), not 401.
        using var res = await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null);

        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
