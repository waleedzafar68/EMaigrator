using System.Text;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Graph.Models;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphMessageMapperTests
{
    private static Message Sample()
    {
        return new Message
        {
            Id = "AAMkADk0graphid==",
            InternetMessageId = "<abc123@contoso.com>",
            Subject = "Quarterly report",
            ReceivedDateTime = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero),
            SentDateTime = new DateTimeOffset(2026, 4, 30, 18, 0, 0, TimeSpan.Zero),
            IsRead = true,
            IsDraft = false,
            Flag = new FollowupFlag { FlagStatus = FollowupFlagStatus.Flagged },
            Categories = new List<string> { "Red", "Finance" },
            Body = new ItemBody { Content = "hello" },
            Attachments = new List<Attachment>
            {
                new FileAttachment { Name = "q.pdf", ContentType = "application/pdf", Size = 2048 }
            }
        };
    }

    private static Task<Stream> OpenMime(CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.ASCII.GetBytes("Message-ID: <abc123@contoso.com>\r\n\r\nbody")));

    [Fact]
    public void Maps_identity_and_message_id_from_internet_message_id()
    {
        var msg = GraphMessageMapper.ToCanonical(Sample(), OpenMime);

        msg.MessageId.Should().Be("<abc123@contoso.com>");
        msg.IdentityKey.Should().StartWith("mid:");
    }

    [Fact]
    public void Maps_received_date_to_internal_date()
    {
        GraphMessageMapper.ToCanonical(Sample(), OpenMime)
            .InternalDate.Should().Be(new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Falls_back_to_sent_date_then_epoch_when_received_is_null()
    {
        var s = Sample();
        s.ReceivedDateTime = null;
        GraphMessageMapper.ToCanonical(s, OpenMime)
            .InternalDate.Should().Be(new DateTimeOffset(2026, 4, 30, 18, 0, 0, TimeSpan.Zero));

        s.SentDateTime = null;
        GraphMessageMapper.ToCanonical(s, OpenMime)
            .InternalDate.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Maps_flags_compositely()
    {
        var msg = GraphMessageMapper.ToCanonical(Sample(), OpenMime);
        msg.Flags.Should().HaveFlag(MessageFlags.Seen);
        msg.Flags.Should().HaveFlag(MessageFlags.Flagged);
        msg.Flags.Should().NotHaveFlag(MessageFlags.Draft);
    }

    [Fact]
    public void Maps_categories_to_labels()
    {
        GraphMessageMapper.ToCanonical(Sample(), OpenMime)
            .Labels.Should().BeEquivalentTo(new[] { "Red", "Finance" });
    }

    [Fact]
    public void Maps_attachments_metadata()
    {
        var att = GraphMessageMapper.ToCanonical(Sample(), OpenMime).Attachments.Single();
        att.FileName.Should().Be("q.pdf");
        att.ContentType.Should().Be("application/pdf");
        att.SizeBytes.Should().Be(2048);
    }

    [Fact]
    public async Task OpenContentAsync_invokes_supplied_factory()
    {
        var calls = 0;
        Func<CancellationToken, Task<Stream>> factory = ct => { calls++; return OpenMime(ct); };

        var msg = GraphMessageMapper.ToCanonical(Sample(), factory);
        await using var stream = await msg.OpenContentAsync(CancellationToken.None);

        calls.Should().Be(1);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Contain("Message-ID");
    }
}
