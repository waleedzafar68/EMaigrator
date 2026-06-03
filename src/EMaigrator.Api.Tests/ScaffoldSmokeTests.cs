using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Boots the real composition root (Infrastructure + the API's own MassTransit bus) against the
/// shared Postgres/Redis/RabbitMQ containers and proves the public health endpoint is reachable and
/// healthy. This is the foundation smoke test every later API task builds on.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ScaffoldSmokeTests
{
    private readonly ApiInfraFixture _fx;

    public ScaffoldSmokeTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Health_endpoint_is_public_and_returns_200()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        // The MassTransit bus connects to RabbitMQ asynchronously on host start, so its health check
        // can lag the very first request by ~1s. Poll briefly until the endpoint reports healthy.
        using var res = await GetHealthWhenReadyAsync(client);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        json.Should().NotBeNull("the /health endpoint must return a JSON body");
        json!.Should().ContainKey("status");
    }

    private static async Task<HttpResponseMessage> GetHealthWhenReadyAsync(HttpClient client)
    {
        HttpResponseMessage? res = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            res?.Dispose();
            res = await client.GetAsync(new Uri("/health", UriKind.Relative));
            if (res.StatusCode == HttpStatusCode.OK)
            {
                return res; // live, caller disposes
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        }

        var last = res?.StatusCode;
        res?.Dispose();
        throw new TimeoutException(
            $"/health did not return 200 within ~10s (20 attempts). Last status: {last}.");
    }
}
