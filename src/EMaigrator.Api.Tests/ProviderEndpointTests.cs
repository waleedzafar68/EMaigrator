using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Exercises <c>GET /api/v1/providers</c>: it requires auth (401 without a token) and, authenticated,
/// returns the registered connectors' capabilities. The <c>canBatch</c> derivation is the key assertion:
/// imap → false (per-mailbox auth only), graph → true (GraphAppOAuth), gmail → true
/// (GmailServiceAccountDwd). The test host registers a fake imap + graph + gmail plugin, so all three
/// providers (and both canBatch branches) are covered against the real live endpoint.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProviderEndpointTests
{
    private readonly ApiInfraFixture _fx;

    public ProviderEndpointTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Providers_requires_auth()
    {
        await using var factory = new ApiTestFactory(_fx);
        using var client = factory.CreateClient();

        using var res = await client.GetAsync(new Uri("/api/v1/providers", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = 30_000)]
    public async Task Providers_returns_capabilities_with_correct_canBatch()
    {
        await using var factory = new ApiTestFactory(_fx);
        var (client, _) = await AuthClient.CreateAsync(factory);
        using var _client = client;

        using var res = await client.GetAsync(new Uri("/api/v1/providers", UriKind.Relative));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var providers = await res.Content.ReadFromJsonAsync<List<ProviderCapabilityDto>>();
        providers.Should().NotBeNull();

        var byId = providers!.ToDictionary(p => p.Id, StringComparer.Ordinal);
        byId.Keys.Should().Contain(new[] { "imap", "graph", "gmail" });

        // The canBatch rule: only providers whose SupportedAuth carries an admin/service-account method
        // (GraphAppOAuth / GmailServiceAccountDwd) can run a multi-mailbox batch.
        byId["imap"].CanBatch.Should().BeFalse("IMAP authenticates per mailbox (basic / XOAUTH2)");
        byId["graph"].CanBatch.Should().BeTrue("Graph supports GraphAppOAuth (admin-wide app auth)");
        byId["gmail"].CanBatch.Should().BeTrue("Gmail supports GmailServiceAccountDwd (domain-wide delegation)");

        // The projection carries the auth-method enum names and the source/destination flags.
        byId["imap"].SupportedAuth.Should().Contain("ImapBasic");
        byId["graph"].CanBeDestination.Should().BeTrue();
    }
}
