using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Exercises Task 4 against the real composition root + live containers: PUT connection stores the
/// secret via <c>ISecretStore</c> and NEVER echoes it; POST connection/test returns the provider's
/// verbatim <c>ConnectionTestResult</c> on success and a stable catalog <c>errorCode</c> on a provider
/// failure; and an unknown <c>side</c> is a 400. The fake IMAP plugin + NSubstitute catalog (registered
/// by <see cref="ApiTestFactory"/>) make the connection-test path deterministic — no real IMAP server.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConnectionEndpointsTests : IAsyncDisposable
{
    private readonly ApiTestFactory _factory;

    public ConnectionEndpointsTests(ApiInfraFixture fx)
    {
        ArgumentNullException.ThrowIfNull(fx);
        _factory = new ApiTestFactory(fx).WithFakeImapPlugin();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact(Timeout = 30_000)]
    public async Task Put_connection_stores_secret_and_never_echoes_it()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        using var _client = client;

        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });

        const string secret = "super-secret-app-password-XYZ";
        var put = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new
        {
            auth = "ImapBasic",
            settings = new { host = "imap.mail.us-east-1.awsapps.com", port = "993", accountEmail = "old@biz.com" },
            secret,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadAsStringAsync()).Should().NotContain(secret, "secrets must never appear in responses");
    }

    [Fact(Timeout = 30_000)]
    public async Task Test_connection_returns_ok_result_from_provider()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        using var _client = client;

        var id = await SetupConnectedDraft(client, FakeImapPlugin.Mode.Ok);

        var res = await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("ok").GetBoolean().Should().BeTrue();
        dto.GetProperty("folderCount").GetInt32().Should().Be(14);
        dto.GetProperty("messageCount").GetInt64().Should().Be(3201);
    }

    [Fact(Timeout = 30_000)]
    public async Task Test_connection_resolves_the_secret_under_the_connector_key()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        using var _client = client;

        FakeImapPlugin.CurrentMode = FakeImapPlugin.Mode.Ok;
        FakeImapPlugin.LastSecrets = null;

        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new
        {
            auth = "ImapBasic",
            settings = new { host = "h", port = "993", accountEmail = "a@b.c" },
            secret = "pw-123",
        });

        var res = await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // The API must store the secret as connector-shaped JSON and resolve it the same way the worker
        // run path does — so the connector sees Values["password"], never a {"secret":…} bundle no
        // connector reads. This is the regression guard for the connect-test/run secret-shape mismatch.
        FakeImapPlugin.LastSecrets.Should().NotBeNull();
        FakeImapPlugin.LastSecrets!.Should().ContainKey("password").WhoseValue.Should().Be("pw-123");
        FakeImapPlugin.LastSecrets.Should().NotContainKey("secret");
    }

    [Fact(Timeout = 30_000)]
    public async Task Test_connection_maps_provider_failure_to_catalog_error_code()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        using var _client = client;

        var id = await SetupConnectedDraft(client, FakeImapPlugin.Mode.AuthFail);

        var res = await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("ok").GetBoolean().Should().BeFalse();
        dto.GetProperty("errorCode").GetString().Should().Be("IMAP_AUTH_FAILED");
    }

    [Fact(Timeout = 30_000)]
    public async Task Put_connection_with_bad_side_returns_400()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        using var _client = client;

        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var put = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/sideways",
            new { auth = "ImapBasic", settings = new { host = "h" }, secret = "x" });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> SetupConnectedDraft(HttpClient client, FakeImapPlugin.Mode mode)
    {
        FakeImapPlugin.CurrentMode = mode;
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new
        {
            auth = "ImapBasic",
            settings = new { host = "h", port = "993", accountEmail = "a@b.c" },
            secret = "pw",
        });
        return id;
    }
}
