using System.Globalization;
using System.Text;
using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailDestinationProviderTests
{
    private static void StubLabels(GmailWireMockFixture fx, string body) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(body));

    private static CanonicalMessage Msg(MessageFlags flags = MessageFlags.None, IReadOnlyList<string>? labels = null)
    {
        var raw = Encoding.UTF8.GetBytes("Message-ID: <acme-001@example.com>\r\nSubject: x\r\n\r\nbody");
        return new CanonicalMessage
        {
            IdentityKey = "mid:<acme-001@example.com>",
            MessageId = "<acme-001@example.com>",
            InternalDate = DateTimeOffset.Parse("2024-05-31T20:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Flags = flags,
            Labels = labels ?? Array.Empty<string>(),
            OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(raw, writable: false)),
        };
    }

    [Fact]
    public async Task EnsureFolderAsync_CreatesLabelWhenAbsent()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx, GmailWireMockFixture.Fixture("labels.list.json"));
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("labels.create.json")));
        var sut = new GmailDestinationProvider(fx.CreateService(), "me");

        await sut.EnsureFolderAsync(FolderPath.Parse("Migrated/Acme"), CancellationToken.None);

        fx.Server.LogEntries.Should().Contain(e =>
            e.RequestMessage!.Method == "POST" && e.RequestMessage!.Path == "/gmail/v1/users/me/labels");
    }

    [Fact]
    public async Task EnsureFolderAsync_NoOpWhenLabelExists()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx, GmailWireMockFixture.Fixture("labels.list.json"));
        var sut = new GmailDestinationProvider(fx.CreateService(), "me");

        await sut.EnsureFolderAsync(FolderPath.Parse("Work/Clients/Acme"), CancellationToken.None);

        fx.Server.LogEntries.Should().NotContain(e => e.RequestMessage!.Method == "POST");
    }

    [Fact]
    public async Task WriteMessageAsync_ImportsRawWithDateHeaderAndLabels()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx, GmailWireMockFixture.Fixture("labels.list.json"));
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.import.json")));
        var sut = new GmailDestinationProvider(fx.CreateService(), "me");

        var result = await sut.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), Msg(MessageFlags.Seen), CancellationToken.None);

        result.Written.Should().BeTrue();
        result.DestMessageId.Should().Be("18f0bb33dd44ee01");
        var import = Array.Find(
            fx.Server.LogEntries.ToArray(),
            e => e.RequestMessage!.Path == "/gmail/v1/users/me/messages/import");
        import.Should().NotBeNull();
        import!.RequestMessage!.RawQuery.Should().Contain("internalDateSource=dateHeader");
    }

    [Fact]
    public async Task WriteMessageAsync_UnseenAddsUnreadLabel()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx, GmailWireMockFixture.Fixture("labels.list.json"));
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.import.json")));
        var sut = new GmailDestinationProvider(fx.CreateService(), "me");

        await sut.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), Msg(MessageFlags.None), CancellationToken.None);

        var import = Array.Find(
            fx.Server.LogEntries.ToArray(),
            e => e.RequestMessage!.Path == "/gmail/v1/users/me/messages/import");
        import!.RequestMessage!.Body.Should().Contain("UNREAD");
    }

    [Fact]
    public async Task WriteMessageAsync_On429_ReturnsErrorCodeWithoutThrowing()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx, GmailWireMockFixture.Fixture("labels.list.json"));
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(429)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("error.429.json")));
        var sut = new GmailDestinationProvider(fx.CreateService(), "me");

        var result = await sut.WriteMessageAsync(FolderPath.Parse("Work/Clients/Acme"), Msg(MessageFlags.Seen), CancellationToken.None);

        result.Written.Should().BeFalse();
        result.ErrorCode.Should().Be("gmail:429:rateLimitExceeded");
    }

    [Fact]
    public async Task ExistsByMessageIdAsync_TrueWhenSearchNonEmpty()
    {
        using var fx = new GmailWireMockFixture();
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.list.json")));
        var sut = new GmailDestinationProvider(fx.CreateService(), "me");

        var exists = await sut.ExistsByMessageIdAsync(FolderPath.Parse("Work"), "<acme-001@example.com>", CancellationToken.None);

        exists.Should().BeTrue();
        var search = Array.Find(
            fx.Server.LogEntries.ToArray(),
            e => e.RequestMessage!.Path == "/gmail/v1/users/me/messages");
        search!.RequestMessage!.RawQuery.Should().Contain("rfc822msgid");
    }

    [Fact]
    public async Task ExistsByMessageIdAsync_FalseWhenSearchEmpty()
    {
        using var fx = new GmailWireMockFixture();
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody("{\"resultSizeEstimate\":0}"));
        var sut = new GmailDestinationProvider(fx.CreateService(), "me");

        (await sut.ExistsByMessageIdAsync(FolderPath.Parse("Work"), "<missing@example.com>", CancellationToken.None))
            .Should().BeFalse();
    }
}
