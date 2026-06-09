using System.Globalization;
using System.Text;
using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailSourceProviderTests
{
    private static void StubLabels(GmailWireMockFixture fx) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("labels.list.json")));

    private static void StubProfile(GmailWireMockFixture fx, long messagesTotal) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/profile").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json")
              .WithBody($"{{\"emailAddress\":\"me@example.com\",\"messagesTotal\":{messagesTotal},\"threadsTotal\":10,\"historyId\":\"1\"}}"));

    private static void StubMessagesList(GmailWireMockFixture fx) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.list.json")));

    private static void StubMessageGet(GmailWireMockFixture fx) =>
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/*").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/json").WithBody(GmailWireMockFixture.Fixture("messages.get.raw.json")));

    [Fact]
    public void Id_And_Constraints_AreGmail()
    {
        using var fx = new GmailWireMockFixture();
        var sut = new GmailSourceProvider(fx.CreateService(), "me");
        sut.Id.Should().Be(new ProviderId("gmail"));
        sut.Constraints.Should().BeSameAs(GmailConstraints.Default);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsOkAndFolderCount()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx);
        StubProfile(fx, 4242);
        var sut = new GmailSourceProvider(fx.CreateService(), "me");

        var result = await sut.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.FolderCount.Should().Be(7); // mappable labels (CHAT + UNREAD excluded): INBOX,SENT,STARRED,CATEGORY_PROMOTIONS,Work,Work/Clients,Work/Clients/Acme
        // The message count comes from users.getProfile.messagesTotal — the true mailbox total. It must
        // NOT sum per-label counts: labels.list omits messagesTotal (→ 0 live), and a sum would double-count
        // messages carrying multiple labels (INBOX + a user label).
        result.MessageCount.Should().Be(4242);
    }

    [Fact]
    public async Task TestConnectionAsync_On401_ReturnsErrorCodeWithoutMailbox()
    {
        using var fx = new GmailWireMockFixture();
        fx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
          .RespondWith(Response.Create().WithStatusCode(401)
              .WithHeader("Content-Type", "application/json")
              .WithBody("{\"error\":{\"code\":401,\"message\":\"Invalid Credentials for victim@example.com\",\"errors\":[{\"reason\":\"authError\",\"message\":\"Invalid Credentials\"}]}}"));
        var sut = new GmailSourceProvider(fx.CreateService(), "me");

        var result = await sut.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("gmail:401:authError");
        (result.RawDetail ?? "").Should().NotContain("@");
    }

    [Fact]
    public async Task ListFoldersAsync_MapsMappableLabels()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx);
        var sut = new GmailSourceProvider(fx.CreateService(), "me");

        var folders = await sut.ListFoldersAsync(CancellationToken.None);

        folders.Select(f => f.Path.ToString()).Should().NotContain("CHAT");
        folders.Select(f => f.Path.ToString()).Should().Contain("Work/Clients/Acme");
        folders.Should().HaveCount(7);
    }

    [Fact]
    public async Task ReadMessagesAsync_YieldsCanonicalMessageFromRaw()
    {
        using var fx = new GmailWireMockFixture();
        StubLabels(fx);
        StubMessagesList(fx);
        StubMessageGet(fx);
        var sut = new GmailSourceProvider(fx.CreateService(), "me");

        var msgs = new List<CanonicalMessage>();
        await foreach (var m in sut.ReadMessagesAsync(FolderPath.Parse("Work/Clients/Acme"), new(), CancellationToken.None))
        {
            msgs.Add(m);
        }

        msgs.Should().NotBeEmpty();
        var first = msgs[0];
        first.IdentityKey.Should().StartWith("mid:");
        first.InternalDate.Should().Be(DateTimeOffset.Parse("2024-05-31T20:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        first.Flags.Should().NotHaveFlag(MessageFlags.Seen);
        first.Labels.Should().Contain("Work/Clients/Acme");

        await using var stream = await first.OpenContentAsync(CancellationToken.None);
        stream.Should().BeOfType<MemoryStream>();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var rfc822 = await reader.ReadToEndAsync();
        rfc822.Should().Contain("Hello Acme");
    }
}
