using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tenancy;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Task 7: <c>POST /migrations/{id}/preflight</c> returns 202 and flips the Job to PreFlight; an inline
/// background runner (test double of <see cref="EMaigrator.Api.Services.IBackgroundTaskQueue"/>) executes
/// the fake analyzer synchronously so <c>GET /migrations/{id}/preflight</c> returns the persisted plan.
/// A GET before any run is 404. Tenant scoping + the anonymous-401 fallback are covered by earlier suites.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PreflightEndpointTests
{
    private readonly ApiTestFactory _factory;

    public PreflightEndpointTests(ApiInfraFixture fx) =>
        _factory = new ApiTestFactory(fx).WithFakeImapPlugin().WithFakePreflight();

    private static async Task<string> ReadyToPreflight(HttpClient c)
    {
        var created = await c.PostAsJsonAsync("/api/v1/migrations", new { });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        await c.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from",
            new { auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret = "pw" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/to",
            new { auth = "ImapBasic", settings = new { host = "h2", port = "993", accountEmail = "d@e.f" }, secret = "pw2" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/scope",
            new { isBatch = false, pairs = new[] { new { sourceMailbox = "a@b.c", destMailbox = "d@e.f" } } });
        return id;
    }

    [Fact]
    public async Task Get_preflight_before_run_returns_404()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ReadyToPreflight(client);
        (await client.GetAsync($"/api/v1/migrations/{id}/preflight")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_preflight_is_202_then_get_returns_plan()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ReadyToPreflight(client);

        using var post = await client.PostAsync($"/api/v1/migrations/{id}/preflight", null);
        post.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The test queue runs inline, so the plan is ready synchronously.
        using var get = await client.GetAsync($"/api/v1/migrations/{id}/preflight");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await get.Content.ReadFromJsonAsync<JsonElement>();
        plan.GetProperty("estimate").GetProperty("messageCount").GetInt64().Should().Be(3201);
        plan.GetProperty("issues").GetArrayLength().Should().Be(1);
        plan.GetProperty("issues")[0].GetProperty("recommendedAction").GetString().Should().Be("FlattenFolder");
        // A stored plan means the scan is done.
        plan.GetProperty("scanning").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_preflight_while_scanning_returns_200_with_scanning_true()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await ReadyToPreflight(client);

        // Simulate "scan in flight": flip the Job to PreFlight WITHOUT running the analyzer, so no
        // PreflightResultRow exists yet.
        SetJobStatusToPreFlight(tenantId, Guid.Parse(id));

        using var get = await client.GetAsync($"/api/v1/migrations/{id}/preflight");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await get.Content.ReadFromJsonAsync<JsonElement>();
        plan.GetProperty("scanning").GetBoolean().Should().BeTrue();
        plan.GetProperty("issues").GetArrayLength().Should().Be(0);
        plan.GetProperty("estimate").GetProperty("messageCount").GetInt64().Should().Be(0);
    }

    private void SetJobStatusToPreFlight(Guid tenantId, Guid jobId)
    {
        using var scope = _factory.Services.CreateScope();
        ((TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>()).Current = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<EmaigratorDbContext>();
        var job = db.Jobs.Single(j => j.Id == jobId);
        job.Status = JobStatus.PreFlight;
        db.SaveChanges();
    }

    [Fact]
    public async Task Post_preflight_cross_tenant_id_returns_404()
    {
        var (owner, _) = await AuthClient.CreateAsync(_factory);
        var id = await ReadyToPreflight(owner);

        var (other, _) = await AuthClient.CreateAsync(_factory);
        using var post = await other.PostAsync($"/api/v1/migrations/{id}/preflight", null);
        post.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
