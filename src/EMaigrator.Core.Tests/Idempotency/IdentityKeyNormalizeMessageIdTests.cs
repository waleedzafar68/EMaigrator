using EMaigrator.Core.Idempotency;

namespace EMaigrator.Core.Tests.Idempotency;

public class IdentityKeyNormalizeMessageIdTests
{
    [Theory]
    [InlineData("<A@X.com>", "a@x.com")]
    [InlineData("  <b@y> ", "b@y")]
    [InlineData("c@z", "c@z")]
    public void Strips_one_bracket_pair_lowercases_and_trims(string input, string expected)
        => IdentityKey.NormalizeMessageId(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_null_for_empty_or_whitespace(string? input)
        => IdentityKey.NormalizeMessageId(input).Should().BeNull();
}
