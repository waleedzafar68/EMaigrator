using System.Reflection;
using System.Text;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Tests.Model;

public class CanonicalModelTests
{
    [Fact]
    public void MessageFlags_Compose()
    {
        var f = MessageFlags.Seen | MessageFlags.Flagged;
        f.HasFlag(MessageFlags.Seen).Should().BeTrue();
        f.HasFlag(MessageFlags.Flagged).Should().BeTrue();
        f.HasFlag(MessageFlags.Draft).Should().BeFalse();
        ((int)MessageFlags.Deleted).Should().Be(16);
    }

    [Fact]
    public void Attachment_IsValueEqual()
    {
        new CanonicalAttachmentInfo("a.pdf", "application/pdf", 10)
            .Should().Be(new CanonicalAttachmentInfo("a.pdf", "application/pdf", 10));
    }

    [Fact]
    public async Task CanonicalMessage_OpensContentStreamOnDemand()
    {
        var payload = "From: a@b.com\r\nSubject: hi\r\n\r\nbody"u8.ToArray();
        var msg = new CanonicalMessage
        {
            IdentityKey = "mid:<x@y>",
            InternalDate = DateTimeOffset.UnixEpoch,
            OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(payload)),
        };

        msg.Labels.Should().BeEmpty();
        msg.Attachments.Should().BeEmpty();

        await using var s = await msg.OpenContentAsync(CancellationToken.None);
        using var reader = new StreamReader(s, Encoding.UTF8);
        var read = await reader.ReadToEndAsync();
        read.Should().Be("From: a@b.com\r\nSubject: hi\r\n\r\nbody");
    }

    [Fact]
    public void CanonicalMessage_HasNoBodyHoldingProperty()
    {
        var props = typeof(CanonicalMessage).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        props.Should().NotContain(p =>
            p.PropertyType == typeof(byte[]) ||
            p.PropertyType == typeof(Stream) ||
            p.PropertyType == typeof(Memory<byte>) ||
            p.PropertyType == typeof(ReadOnlyMemory<byte>));
        props.Should().NotContain(p =>
            string.Equals(p.Name, "Body", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, "Content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CanonicalFolder_Constructs()
    {
        var folder = new CanonicalFolder(FolderPath.Parse("Inbox"), 42, MessageFlags.Seen);
        folder.Path.Name.Should().Be("Inbox");
        folder.EstimatedMessageCount.Should().Be(42);
        folder.SpecialUse.Should().Be(MessageFlags.Seen);

        new CanonicalFolder(FolderPath.Parse("Sent"), 3).SpecialUse.Should().BeNull();
    }
}
