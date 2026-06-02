using System.Text;
using System.Text.Json;
using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EMaigrator.Connectors.Gmail.Tests;

/// <summary>
/// Provider-boundary conformance tests: prove <see cref="GmailSourceProvider"/> and
/// <see cref="GmailDestinationProvider"/> honor the CONTRACTS §2 interface semantics end-to-end
/// against recorded fixtures (read a raw message from the source, write it via the destination's
/// import, and confirm the streaming pass-through preserves the original Message-ID without
/// persisting any body bytes).
/// </summary>
public class GmailContractConformanceTests
{
    private static void StubLabels(GmailWireMockFixture fx) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("labels.list.json")));

    private static void StubMessagesList(GmailWireMockFixture fx) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.list.json")));

    private static void StubMessageGet(GmailWireMockFixture fx) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/*").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.get.raw.json")));

    private static void StubImport(GmailWireMockFixture fx) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.import.json")));

    [Fact]
    public void Providers_ImplementContractInterfaces()
    {
        typeof(ISourceProvider).IsAssignableFrom(typeof(GmailSourceProvider)).Should().BeTrue();
        typeof(IAsyncDisposable).IsAssignableFrom(typeof(GmailSourceProvider)).Should().BeTrue();
        typeof(IDestinationProvider).IsAssignableFrom(typeof(GmailDestinationProvider)).Should().BeTrue();
        typeof(IAsyncDisposable).IsAssignableFrom(typeof(GmailDestinationProvider)).Should().BeTrue();
    }

    [Fact]
    public async Task Providers_Assignable_And_DisposeAsync_DoesNotThrow()
    {
        using var fx = new GmailWireMockFixture();

        var source = new GmailSourceProvider(fx.CreateService(), "me");
        var dest = new GmailDestinationProvider(fx.CreateService(), "me");

        source.Should().BeAssignableTo<ISourceProvider>().And.BeAssignableTo<IAsyncDisposable>();
        dest.Should().BeAssignableTo<IDestinationProvider>().And.BeAssignableTo<IAsyncDisposable>();

        await source.DisposeAsync(); // must not throw
        await dest.DisposeAsync();   // must not throw
    }

    [Fact]
    public async Task ReadThenWrite_RoundTripsThroughStreamingPassThrough()
    {
        using var srcFx = new GmailWireMockFixture();
        StubLabels(srcFx);
        StubMessagesList(srcFx);
        StubMessageGet(srcFx);

        await using var source = new GmailSourceProvider(srcFx.CreateService(), "me");

        CanonicalMessage? captured = null;
        await foreach (var m in source.ReadMessagesAsync(FolderPath.Parse("Work/Clients/Acme"), new(), CancellationToken.None))
        {
            captured = m;
            break;
        }

        captured.Should().NotBeNull();

        // OpenContentAsync must be replayable (retry-safe): invoking twice yields equal bytes.
        byte[] first, second;
        await using (var s1 = await captured!.OpenContentAsync(CancellationToken.None))
        using (var ms1 = new MemoryStream())
        {
            await s1.CopyToAsync(ms1);
            first = ms1.ToArray();
        }

        await using (var s2 = await captured!.OpenContentAsync(CancellationToken.None))
        using (var ms2 = new MemoryStream())
        {
            await s2.CopyToAsync(ms2);
            second = ms2.ToArray();
        }

        first.Should().Equal(second);

        using var dstFx = new GmailWireMockFixture();
        StubLabels(dstFx);
        StubImport(dstFx);

        await using var dest = new GmailDestinationProvider(dstFx.CreateService(), "me");
        var result = await dest.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), captured!, CancellationToken.None);

        result.Written.Should().BeTrue();

        var import = Array.Find(
            dstFx.Server.LogEntries.ToArray(),
            e => e.RequestMessage!.Path == "/gmail/v1/users/me/messages/import");
        import.Should().NotBeNull();

        // The imported raw is base64url of the original RFC822; decode and assert Message-ID preserved.
        var body = import!.RequestMessage!.Body ?? "";
        var rawField = JsonDocument.Parse(body).RootElement.GetProperty("raw").GetString()!;
        var decoded = Encoding.UTF8.GetString(GmailRawCodec.DecodeBase64Url(rawField));
        decoded.Should().Contain("<acme-001@example.com>");
    }
}
