using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Test helper that provisions a fully-authenticated <see cref="HttpClient"/> against the in-process
/// API: it registers a fresh tenant + user (unique email), logs in, and attaches the resulting bearer
/// token. The returned tuple also carries the new tenant id so tests can assert tenant scoping.
/// <para>
/// Every client also sends an <c>X-Client-Id</c> header (a random GUID), forward-compat for the Task 12
/// auth rate-limiter bucket so repeated registrations across tests do not share a throttle key.
/// </para>
/// </summary>
public static class AuthClient
{
    /// <summary>The password used for every test user; satisfies the 12-char Identity policy.</summary>
    public const string Password = "Sup3rSecret!Pass";

    /// <summary>
    /// Creates a client, registers a fresh tenant + user, logs in, and returns the bearer-authed client
    /// alongside the created tenant id.
    /// </summary>
    public static async Task<(HttpClient Client, Guid TenantId)> CreateAsync(ApiTestFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", Guid.NewGuid().ToString("N"));

        var email = $"user-{Guid.NewGuid():N}@example.com";

        using var register = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, password = Password, organizationName = "Acme" });
        register.EnsureSuccessStatusCode();
        var reg = (await register.Content.ReadFromJsonAsync<RegResp>())!;

        using var login = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResp>())!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return (client, reg.TenantId);
    }

    private sealed record RegResp(Guid Id, Guid TenantId);

    private sealed record TokenResp(string AccessToken, DateTimeOffset ExpiresAt);
}
