using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Api.Tests.Security;

/// <summary>
/// Input validation: malformed bodies (bad auth enum, unknown side, blank CSV, invalid register payload)
/// must each return a client error in the 400–422 band — never a 500. A 500 would mean the API faulted on
/// untrusted input instead of rejecting it cleanly. Captures the observed status per case.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InputValidationSweepTests
{
    private readonly ApiInfraFixture _fx;
    private readonly ITestOutputHelper _out;

    public InputValidationSweepTests(ApiInfraFixture fx, ITestOutputHelper o)
    {
        _fx = fx;
        _out = o;
    }

    [Fact(Timeout = 60_000)]
    public async Task Malformed_bodies_return_4xx_never_500()
    {
        await using var factory = new ApiTestFactory(_fx).WithFakeImapPlugin();
        var (client, _) = await AuthClient.CreateAsync(factory);
        using var _client = client;

        using var created = await client.PostAsJsonAsync(new Uri("/api/v1/migrations", UriKind.Relative), new { });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Bad enum auth + unknown side.
        using var badAuth = await client.PutAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/connection/from", UriKind.Relative),
            new { auth = "NotAnAuthMethod", settings = new { host = "h" }, secret = "x" });
        using var badSide = await client.PutAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/connection/sideways", UriKind.Relative),
            new { auth = "ImapBasic", settings = new { host = "h" }, secret = "x" });

        // Blank CSV upload (header-only, no data rows).
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes("source_mailbox,destination_mailbox\n"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "empty.csv");
        using var _ = await client.PatchAsJsonAsync(
            new Uri($"/api/v1/migrations/{id}/endpoints", UriKind.Relative), new { from = "imap", to = "graph" });
        using var emptyCsv = await client.PutAsync(
            new Uri($"/api/v1/migrations/{id}/scope", UriKind.Relative), content);

        // Malformed register payload (bad email + too-short password).
        using var badReg = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative), new { email = "not-an-email", password = "short" });

        foreach (var (name, res) in new[]
        {
            ("badAuth", badAuth), ("badSide", badSide), ("emptyCsv", emptyCsv), ("badReg", badReg),
        })
        {
            _out.WriteLine($"{name} -> {(int)res.StatusCode}");
            ((int)res.StatusCode).Should().BeInRange(400, 422, $"{name} must be a client error, not 500");
            res.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }
    }
}
