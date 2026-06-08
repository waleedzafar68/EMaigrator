using System.Text;
using EMaigrator.Connectors.Graph.Reconcile;
using FluentAssertions;
using MimeKit;

namespace EMaigrator.Connectors.Graph.Tests.Reconcile;

public class GraphMimeSplitterTests
{
    private static MimeMessage MultipartWithAttachment(int attachmentBytes)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("A", "a@x.com"));
        msg.To.Add(new MailboxAddress("B", "b@y.com"));
        msg.Subject = "big";
        var text = new TextPart("plain") { Text = "hello body" };
        var att = new MimePart("application", "octet-stream")
        {
            Content = new MimeContent(new MemoryStream(new byte[attachmentBytes])),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "big.bin" },
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = "big.bin",
        };
        msg.Body = new Multipart("mixed") { text, att };
        return msg;
    }

    private static MimeMessage Load(string raw)
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(raw));
        return MimeMessage.Load(ms);
    }

    [Fact]
    public void Attachments_enumerates_attachment_parts()
    {
        var atts = GraphMimeSplitter.Attachments(MultipartWithAttachment(1024));
        atts.Should().ContainSingle();
        atts[0].Content.Name.Should().Be("big.bin");
        atts[0].Content.ContentType.Should().Be("application/octet-stream");
        atts[0].Content.Size.Should().Be(1024);
    }

    [Fact]
    public void IsSigned_true_for_pkcs7_mime()
    {
        var raw = "From: a@x.com\r\nSubject: s\r\nMIME-Version: 1.0\r\n" +
                  "Content-Type: application/pkcs7-mime; smime-type=enveloped-data; name=\"smime.p7m\"\r\n" +
                  "Content-Transfer-Encoding: base64\r\n\r\nAAAA\r\n";
        GraphMimeSplitter.IsSigned(Load(raw)).Should().BeTrue();
    }

    [Fact]
    public void IsSigned_false_for_plain_multipart()
    {
        GraphMimeSplitter.IsSigned(MultipartWithAttachment(16)).Should().BeFalse();
    }

    [Fact]
    public void Reduce_strips_largest_part_until_under_limit()
    {
        var msg = MultipartWithAttachment(4 * 1024 * 1024); // 4 MB part forces a strip under a 1 MB limit
        var split = GraphMimeSplitter.Reduce(msg, 1 * 1024 * 1024);

        split.Stripped.Should().ContainSingle();
        split.Stripped[0].Name.Should().Be("big.bin");
        split.Stripped[0].Size.Should().Be(4 * 1024 * 1024);
        ((long)split.ReducedMimeBytes.Length * 4 / 3).Should().BeLessThanOrEqualTo(1 * 1024 * 1024);
        split.IsSigned.Should().BeFalse();
    }
}
