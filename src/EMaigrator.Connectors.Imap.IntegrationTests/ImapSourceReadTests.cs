using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using MimeKit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

[Collection("greenmail")]
public class ImapSourceReadTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapSourceReadTests(GreenMailImapFixture fx) => _fx = fx;

    private ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = _fx.Host,
            ["port"] = _fx.ImapPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };

    private static SecretBundle Secret() =>
        new(new Dictionary<string, string> { ["password"] = GreenMailImapFixture.Password });

    [Fact]
    public async Task TestConnection_reports_folder_and_message_counts()
    {
        var mid = $"<read-conn-{Guid.NewGuid():N}@local.test>";
        await _fx.DeliverToInboxAsync("conn-test", "hi", mid);

        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        var result = await src.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.FolderCount.Should().BeGreaterThanOrEqualTo(1);
        result.MessageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ReadMessages_yields_canonical_message_with_identity_date_flags_and_stream()
    {
        var subject = $"subj-{Guid.NewGuid():N}";
        var mid = $"<read-msg-{Guid.NewGuid():N}@local.test>";
        var date = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        await _fx.AppendAsync("INBOX", subject, "body-text", mid, MailKit.MessageFlags.Seen, date);

        await using var src = new ImapSourceProvider(Descriptor(), Secret());

        CanonicalMessage? found = null;
        await foreach (var m in src.ReadMessagesAsync(FolderPath.Parse("INBOX"), new ReadOptions(), CancellationToken.None))
        {
            if (m.Subject == subject) { found = m; break; }
        }

        found.Should().NotBeNull();
        found!.IdentityKey.Should().StartWith("mid:");
        found.InternalDate.Should().BeCloseTo(date, TimeSpan.FromSeconds(2));
        found.Flags.Should().HaveFlag(MessageFlags.Seen);

        await using var stream = await found.OpenContentAsync(CancellationToken.None);
        var parsed = await MimeMessage.LoadAsync(stream);
        parsed.Subject.Should().Be(subject);
    }

    [Fact]
    public async Task ListFolders_includes_inbox_and_created_subfolder()
    {
        await _fx.AppendAsync("Projects", "p1", "b", $"<proj-{Guid.NewGuid():N}@local.test>",
            MailKit.MessageFlags.None, DateTimeOffset.UtcNow);

        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        var folders = await src.ListFoldersAsync(CancellationToken.None);

        folders.Select(f => f.Path.Name).Should().Contain(new[] { "INBOX", "Projects" });
    }
}
