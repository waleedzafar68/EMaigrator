using System.Text;
using EMaigrator.Connectors.Gmail;
using FluentAssertions;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailRawCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var original = Encoding.UTF8.GetBytes("Subject: x\r\n\r\nbody with + / = chars");
        var encoded = GmailRawCodec.EncodeBase64Url(original);
        encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        GmailRawCodec.DecodeBase64Url(encoded).Should().Equal(original);
    }

    [Fact]
    public void DecodeBase64Url_HandlesMissingPadding()
    {
        // "Hi" => base64 "SGk=" => base64url without padding "SGk"
        GmailRawCodec.DecodeBase64Url("SGk").Should().Equal(Encoding.UTF8.GetBytes("Hi"));
    }
}
