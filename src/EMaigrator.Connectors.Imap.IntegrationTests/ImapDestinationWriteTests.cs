using System.IO;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using MimeKit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

[Collection("greenmail")]
public class ImapDestinationWriteTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapDestinationWriteTests(GreenMailImapFixture fx) => _fx = fx;

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

    private static CanonicalMessage BuildMessage(string subject, string messageId, DateTimeOffset date, MessageFlags flags)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("S", "s@local.test"));
        mime.To.Add(new MailboxAddress("D", GreenMailImapFixture.UserEmail));
        mime.Subject = subject;
        mime.MessageId = messageId.Trim('<', '>');
        mime.Body = new TextPart("plain") { Text = "destination body" };
        var ms = new MemoryStream();
        mime.WriteTo(ms);
        var bytes = ms.ToArray();
        return new CanonicalMessage
        {
            IdentityKey = "mid:" + messageId,
            MessageId = messageId,
            InternalDate = date,
            Flags = flags,
            Subject = subject,
            SizeBytes = bytes.Length,
            OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
        };
    }

    [Fact]
    public async Task EnsureFolder_creates_nested_hierarchy_idempotently()
    {
        var path = new FolderPath(new[] { "Archive", "2026", "Q1" });
        await using var dst = new ImapDestinationProvider(Descriptor(), Secret());

        await dst.EnsureFolderAsync(path, CancellationToken.None);
        await dst.EnsureFolderAsync(path, CancellationToken.None); // idempotent

        // ExistsByMessageId opens the leaf folder — succeeds only if it was created.
        var act = async () => await dst.ExistsByMessageIdAsync(path, "<none@local.test>", CancellationToken.None);
        (await act.Should().NotThrowAsync()).Subject.Should().BeFalse();
    }

    [Fact]
    public async Task WriteMessage_appends_preserving_date_and_flags_and_is_searchable()
    {
        var path = new FolderPath(new[] { "Migrated" });
        var subject = $"dst-{Guid.NewGuid():N}";
        var mid = $"<dst-{Guid.NewGuid():N}@local.test>";
        var date = new DateTimeOffset(2025, 12, 24, 18, 30, 0, TimeSpan.Zero);
        var msg = BuildMessage(subject, mid, date, MessageFlags.Seen | MessageFlags.Flagged);

        await using var dst = new ImapDestinationProvider(Descriptor(), Secret());
        var write = await dst.WriteMessageAsync(path, msg, CancellationToken.None);
        write.Written.Should().BeTrue();

        (await dst.ExistsByMessageIdAsync(path, mid, CancellationToken.None)).Should().BeTrue();
        (await dst.ExistsByMessageIdAsync(path, "<absent@local.test>", CancellationToken.None)).Should().BeFalse();

        // Read it back through the source provider to confirm date+flags survived.
        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        CanonicalMessage? roundtrip = null;
        await foreach (var m in src.ReadMessagesAsync(path, new ReadOptions(), CancellationToken.None))
        {
            if (m.Subject == subject) { roundtrip = m; break; }
        }
        roundtrip.Should().NotBeNull();
        roundtrip!.InternalDate.Should().BeCloseTo(date, TimeSpan.FromSeconds(2));
        roundtrip.Flags.Should().HaveFlag(MessageFlags.Seen);
        roundtrip.Flags.Should().HaveFlag(MessageFlags.Flagged);
    }
}
