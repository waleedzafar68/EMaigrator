using System.Text;
using EMaigrator.Connectors.Gmail;
using FluentAssertions;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailMimeAttachmentsTests
{
    private const string Multipart =
        "From: a@x.com\r\nTo: b@y.com\r\nSubject: hi\r\nMIME-Version: 1.0\r\n" +
        "Content-Type: multipart/mixed; boundary=\"B\"\r\n\r\n--B\r\n" +
        "Content-Type: text/plain\r\n\r\nbody\r\n--B\r\n" +
        "Content-Type: application/pdf; name=\"doc.pdf\"\r\n" +
        "Content-Disposition: attachment; filename=\"doc.pdf\"\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\naGVsbG8=\r\n--B--\r\n";

    [Fact]
    public void Read_enumerates_attachment_parts()
    {
        var infos = GmailMimeAttachments.Read(Encoding.ASCII.GetBytes(Multipart));
        infos.Should().ContainSingle();
        infos[0].FileName.Should().Be("doc.pdf");
        infos[0].ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public void Read_returns_empty_for_plain_message()
    {
        var plain = Encoding.ASCII.GetBytes("From: a@x.com\r\nSubject: s\r\n\r\njust text\r\n");
        GmailMimeAttachments.Read(plain).Should().BeEmpty();
    }
}
