using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// End-to-end coverage for <c>PUT /api/v1/migrations/{id}/scope</c> over the live-container harness: a
/// JSON single pair persists one non-batch mailbox and advances the wizard; a multipart CSV upload
/// persists the batch; and an invalid (duplicate-source) CSV returns 400 naming the offending row.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ScopeEndpointTests : IAsyncDisposable
{
    private readonly ApiTestFactory _factory;

    public ScopeEndpointTests(ApiInfraFixture fx)
    {
        ArgumentNullException.ThrowIfNull(fx);
        _factory = new ApiTestFactory(fx).WithFakeImapPlugin();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<string> NewDraft(HttpClient c)
    {
        var id = (await (await c.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await c.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        return id;
    }

    [Fact]
    public async Task Json_single_scope_persists_one_mailbox()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);
        var res = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/scope", new
        {
            isBatch = false,
            pairs = new[] { new { sourceMailbox = "old@biz.com", destMailbox = "new@biz.com" } },
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("isBatch").GetBoolean().Should().BeFalse();
        dto.GetProperty("mailboxCount").GetInt32().Should().Be(1);
        dto.GetProperty("wizardStep").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Json_single_scope_with_empty_pairs_derives_one_mailbox_from_connections()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);

        // Single mode never types a pair in Scope — the one mailbox is derived from the connections'
        // accountEmail. Configure both sides first.
        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new
        {
            auth = "ImapBasic",
            settings = new { host = "imap.example.com", port = "993", region = "us-east-1", accountEmail = "alice@source.com" },
            secret = "pw",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/to", new
        {
            auth = "GraphAppOAuth",
            settings = new { tenantId = "t", clientId = "c", accountEmail = "alice@dest.com" },
            secret = "cs",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/scope", new
        {
            isBatch = false,
            pairs = Array.Empty<object>(),
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("isBatch").GetBoolean().Should().BeFalse();
        dto.GetProperty("mailboxCount").GetInt32().Should().Be(1, "single mode creates exactly one mailbox row");
        dto.GetProperty("scopeSummary").GetString().Should().Contain("alice@source.com").And.Contain("alice@dest.com");
        dto.GetProperty("wizardStep").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Json_single_scope_with_empty_pairs_and_no_connections_returns_400()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);

        // No connections configured → nothing to derive the mailbox from → an honest 400 (not a 500/silent no-op).
        var res = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/scope", new
        {
            isBatch = false,
            pairs = Array.Empty<object>(),
        });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Multipart_csv_persists_batch()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);
        using var content = new MultipartFormDataContent();
        var csv = "source_mailbox,destination_mailbox\na@old.com,a@new.com\nb@old.com,b@new.com\nc@old.com,c@new.com\n";
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "mailboxes.csv");

        var res = await client.PutAsync($"/api/v1/migrations/{id}/scope", content);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("isBatch").GetBoolean().Should().BeTrue();
        dto.GetProperty("mailboxCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Invalid_csv_returns_400_with_row_error()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);
        using var content = new MultipartFormDataContent();
        var csv = "source_mailbox,destination_mailbox\na@old.com,a@new.com\na@old.com,c@new.com\n";
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "dupes.csv");
        var res = await client.PutAsync($"/api/v1/migrations/{id}/scope", content);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("duplicate");
    }
}
