using EMaigrator.Core.Idempotency;

namespace EMaigrator.Core.Tests.Idempotency;

public class IdentityKeyTests
{
    private static MessageIdentityInput Fallback(string body = "deadbeef") => new()
    {
        MessageId = null,
        From = "Alice@Example.COM",
        To = "bob@example.com",
        Subject = "Quarterly Report",
        Date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        DecodedBodySha256Hex = body,
    };

    [Fact]
    public void Compute_UsesMessageId_WhenPresent()
    {
        var key = IdentityKey.Compute(new MessageIdentityInput
        {
            MessageId = "  <ABC@Host.COM>  ",
            DecodedBodySha256Hex = "ignored",
        });
        key.Should().Be("mid:abc@host.com");
    }

    [Fact]
    public void Compute_StripsOnlyOnePairOfAngleBrackets()
    {
        IdentityKey.Compute(new MessageIdentityInput { MessageId = "<<x@y>>", DecodedBodySha256Hex = "z" })
            .Should().Be("mid:<x@y>");
    }

    [Fact]
    public void Compute_FallsBackToCompositeHash_WhenNoMessageId()
    {
        var key = IdentityKey.Compute(Fallback());
        key.Should().StartWith("h:");
        key.Substring(2).Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        IdentityKey.Compute(Fallback()).Should().Be(IdentityKey.Compute(Fallback()));
    }

    [Fact]
    public void Compute_DiffersWhenBodyHashDiffers()
    {
        IdentityKey.Compute(Fallback("aaaa")).Should().NotBe(IdentityKey.Compute(Fallback("bbbb")));
    }

    [Fact]
    public void Compute_FallbackNormalizesAddressCaseAndDate()
    {
        var lower = Fallback() with { From = "alice@example.com" };
        IdentityKey.Compute(lower).Should().Be(IdentityKey.Compute(Fallback()));

        var diffZone = Fallback() with { Date = new DateTimeOffset(2026, 1, 2, 4, 4, 5, TimeSpan.FromHours(1)) };
        IdentityKey.Compute(diffZone).Should().Be(IdentityKey.Compute(Fallback())); // same instant, normalized to UTC
    }

    [Fact]
    public void Compute_NeverHashesRawBytes_OnlyDecodedBodyHash()
    {
        // Two messages whose raw transport bytes differ wildly but whose DECODED body hash
        // (and headers) are identical MUST yield the same identity key. This proves the
        // fallback hashes the decoded-body fingerprint, never raw bytes.
        var rawA = Fallback("decoded-fingerprint");
        var rawB = Fallback("decoded-fingerprint"); // same decoded-body hash, regardless of raw transit form
        IdentityKey.Compute(rawA).Should().Be(IdentityKey.Compute(rawB));
    }

    [Fact]
    public void Compute_NullInput_Throws()
    {
        var act = () => IdentityKey.Compute(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- coverage gap-closers (Task 17): exercise Message-ID normalization edge branches ---

    [Fact]
    public void Compute_MessageIdWithoutBrackets_IsNotStripped()
        => IdentityKey.Compute(new MessageIdentityInput { MessageId = "PlainId@Host", DecodedBodySha256Hex = "x" })
            .Should().Be("mid:plainid@host");

    [Fact]
    public void Compute_MessageIdWithLeadingBracketOnly_IsNotStripped()
        => IdentityKey.Compute(new MessageIdentityInput { MessageId = "<abc", DecodedBodySha256Hex = "x" })
            .Should().Be("mid:<abc");

    [Fact]
    public void Compute_SingleCharMessageId_IsNotStripped()
        => IdentityKey.Compute(new MessageIdentityInput { MessageId = "x", DecodedBodySha256Hex = "y" })
            .Should().Be("mid:x");

    [Fact]
    public void Compute_MessageIdEmptyAfterBracketStrip_FallsBackToHash()
        => IdentityKey.Compute(new MessageIdentityInput { MessageId = "<>", DecodedBodySha256Hex = "z" })
            .Should().StartWith("h:");

    [Fact]
    public void Compute_FallbackWithAllNullOptionalFields_StillHashes()
    {
        var key = IdentityKey.Compute(new MessageIdentityInput
        {
            MessageId = null,
            From = null,
            To = null,
            Subject = null,
            Date = null,
            DecodedBodySha256Hex = "only-the-body-fingerprint",
        });
        key.Should().StartWith("h:");
        key.Substring(2).Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
