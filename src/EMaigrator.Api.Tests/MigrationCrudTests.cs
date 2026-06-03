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
/// Exercises the Task 3 tenant-scoped migrations CRUD + draft endpoints against the real composition
/// root + live Postgres: create persists a Draft with the exact camelCase <c>MigrationDto</c> shape,
/// PATCH endpoints sets providers and advances the wizard, one tenant cannot read another's migration
/// (404), the list filters by status and DELETE discards a draft, and an unauthenticated POST is 401.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MigrationCrudTests
{
    private static readonly Uri MigrationsUri = new("/api/v1/migrations", UriKind.Relative);

    private readonly ApiInfraFixture _fx;

    public MigrationCrudTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Post_creates_draft_with_expected_dto_shape()
    {
        await using var factory = new ApiTestFactory(_fx);
        var (client, _) = await AuthClient.CreateAsync(factory);
        using var _client = client;

        using var response = await client.PostAsJsonAsync(MigrationsUri, new { });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();

        dto.GetProperty("status").GetString().Should().Be("Draft");
        dto.GetProperty("wizardStep").GetInt32().Should().Be(1);

        foreach (var key in new[]
                 {
                     "id", "status", "wizardStep", "from", "to", "isBatch",
                     "scopeSummary", "mailboxCount", "progress", "createdAt",
                 })
        {
            dto.TryGetProperty(key, out _).Should().BeTrue($"the DTO must expose the camelCase key '{key}'");
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Patch_endpoints_sets_providers_and_advances_wizard()
    {
        await using var factory = new ApiTestFactory(_fx);
        var (client, _) = await AuthClient.CreateAsync(factory);
        using var _client = client;

        var id = await CreateDraftAsync(client);

        using var patch = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/endpoints", UriKind.Relative),
            new { from = "imap", to = "graph" });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await patch.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("from").GetString().Should().Be("imap");
        dto.GetProperty("to").GetString().Should().Be("graph");
        dto.GetProperty("wizardStep").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact(Timeout = 30_000)]
    public async Task Get_other_tenants_migration_returns_404()
    {
        await using var factory = new ApiTestFactory(_fx);

        var (clientA, _) = await AuthClient.CreateAsync(factory);
        using var _a = clientA;
        var (clientB, _) = await AuthClient.CreateAsync(factory);
        using var _b = clientB;

        var id = await CreateDraftAsync(clientA);

        using var get = await clientB.GetAsync(new Uri($"/api/v1/migrations/{id}", UriKind.Relative));
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = 30_000)]
    public async Task List_filters_by_status_and_delete_removes()
    {
        await using var factory = new ApiTestFactory(_fx);
        var (client, _) = await AuthClient.CreateAsync(factory);
        using var _client = client;

        var id = await CreateDraftAsync(client);

        using var list = await client.GetAsync(new Uri("/api/v1/migrations?status=Draft", UriKind.Relative));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var array = await list.Content.ReadFromJsonAsync<JsonElement>();
        array.ValueKind.Should().Be(JsonValueKind.Array);
        array.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        using var delete = await client.DeleteAsync(new Uri($"/api/v1/migrations/{id}", UriKind.Relative));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var getAfter = await client.GetAsync(new Uri($"/api/v1/migrations/{id}", UriKind.Relative));
        getAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = 30_000)]
    public async Task Unauthenticated_create_returns_401()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(MigrationsUri, new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient client)
    {
        using var create = await client.PostAsJsonAsync(MigrationsUri, new { });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await create.Content.ReadFromJsonAsync<JsonElement>();
        return dto.GetProperty("id").GetGuid();
    }
}
