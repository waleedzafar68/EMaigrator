using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests.Security;

/// <summary>
/// No-secret guarantee: a stored connection secret is written only to <c>ISecretStore</c> and is never
/// echoed back. This grep-style sweep stores a sentinel secret then asserts it is absent from the bodies
/// of the PUT-connection response, the GET migration, the connection-test, and the audit endpoint.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NoSecretInResponseTests
{
    private readonly ApiInfraFixture _fx;

    public NoSecretInResponseTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 60_000)]
    public async Task Secret_never_appears_in_any_response_body()
    {
        const string secret = "TOPSECRET-app-password-9f3c-DEADBEEF";

        await using var factory = new ApiTestFactory(_fx).WithFakeImapPlugin();
        var (client, _) = await AuthClient.CreateAsync(factory);
        using var _client = client;

        using var created = await client.PostAsJsonAsync(new Uri("/api/v1/migrations", UriKind.Relative), new { });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var _ = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/endpoints", UriKind.Relative), new { from = "imap", to = "graph" });

        using var put = await client.PutAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/connection/from", UriKind.Relative),
            new { auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret });
        var putBody = await put.Content.ReadAsStringAsync();

        using var get = await client.GetAsync(new Uri($"/api/v1/migrations/{id}", UriKind.Relative));
        var getBody = await get.Content.ReadAsStringAsync();

        using var test = await client.PostAsync(
            new Uri($"/api/v1/migrations/{id}/connection/from/test", UriKind.Relative), null);
        var testBody = await test.Content.ReadAsStringAsync();

        using var audit = await client.GetAsync(new Uri($"/api/v1/migrations/{id}/audit", UriKind.Relative));
        var auditBody = await audit.Content.ReadAsStringAsync();

        foreach (var body in new[] { putBody, getBody, testBody, auditBody })
        {
            body.Should().NotContain(secret, "no API response may contain a credential value");
        }
    }
}
