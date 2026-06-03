using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace EMaigrator.Api.Tests.Security;

/// <summary>
/// Tenant isolation: tenant A creates a migration and sets its endpoints; tenant B must receive 404 on
/// every REST verb for that id (the row-level query filter makes it invisible) AND a SignalR
/// <c>Subscribe</c> to it must throw (the hub authorizes off the connection's tenant claim + an explicit
/// predicate). Proves cross-tenant access is denied on both the REST and realtime surfaces.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrossTenantAccessTests
{
    private readonly ApiInfraFixture _fx;

    public CrossTenantAccessTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 60_000)]
    public async Task Tenant_B_cannot_touch_tenant_A_migration_over_rest_or_signalr()
    {
        await using var factory = new ApiTestFactory(_fx).WithFakeImapPlugin();
        var (a, _) = await AuthClient.CreateAsync(factory);
        var (b, _) = await AuthClient.CreateAsync(factory);
        using var _a = a;
        using var _b = b;

        using var created = await a.PostAsJsonAsync(new Uri("/api/v1/migrations", UriKind.Relative), new { });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        using var _ = await a.PatchAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/endpoints", UriKind.Relative), new { from = "imap", to = "graph" });

        foreach (var (method, path) in new[]
        {
            ("GET", $"/api/v1/migrations/{id}"),
            ("POST", $"/api/v1/migrations/{id}/preflight"),
            ("GET", $"/api/v1/migrations/{id}/results"),
            ("GET", $"/api/v1/migrations/{id}/audit"),
            ("DELETE", $"/api/v1/migrations/{id}"),
        })
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
            using var res = await b.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound, $"tenant B must get 404 for {method} {path}");
        }

        using var patch = await b.PatchAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/endpoints", UriKind.Relative), new { from = "imap", to = "gmail" });
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var tokenB = b.DefaultRequestHeaders.Authorization!.Parameter!;
        var conn = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "hubs/migrations?access_token=" + tokenB),
                o => o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();
        await conn.StartAsync();
        var act = async () => await conn.InvokeAsync("Subscribe", id);
        await act.Should().ThrowAsync<Exception>("tenant B must not subscribe to tenant A's migration");
        await conn.DisposeAsync();
    }
}
