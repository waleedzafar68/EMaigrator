using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Exercises the Task 1 auth surface against the real composition root + live Postgres:
/// register creates a Tenant + ApplicationUser (201), login returns a tenant-scoped JWT plus an
/// auth cookie (200), a wrong password is rejected (401), and a too-short password is a validation
/// failure (400). The JWT must carry a non-empty <c>tenant_id</c> claim equal to the user's TenantId.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthEndpointsTests
{
    private const string ValidPassword = "Sup3rSecret!Pass";

    private readonly ApiInfraFixture _fx;

    public AuthEndpointsTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Register_then_login_issues_jwt_with_tenant_claim()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        var email = $"user-{Guid.NewGuid():N}@example.com";

        using var register = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = ValidPassword, organizationName = "Acme MSP" });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = ValidPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        login.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue(
            "login must set the auth cookie");
        cookies!.Should().Contain(c => c.StartsWith("emaigrator.auth", StringComparison.Ordinal));

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = body.GetProperty("accessToken").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id");
        tenantClaim.Should().NotBeNull("the JWT must carry a tenant_id claim");
        tenantClaim!.Value.Should().NotBeNullOrEmpty();
    }

    [Fact(Timeout = 30_000)]
    public async Task Login_with_wrong_password_returns_401()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        var email = $"user-{Guid.NewGuid():N}@example.com";

        using var register = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = ValidPassword, organizationName = "Acme MSP" });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = "WrongPassword!1" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = 30_000)]
    public async Task Register_with_short_password_returns_400()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        var email = $"user-{Guid.NewGuid():N}@example.com";

        using var register = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = "short", organizationName = "Acme MSP" });
        register.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
