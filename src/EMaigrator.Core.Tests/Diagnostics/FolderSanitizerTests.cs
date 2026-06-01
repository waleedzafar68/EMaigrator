using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Tests.Diagnostics;

public class FolderSanitizerTests
{
    public static TheoryData<string, ProviderConstraints, string> Cases() => new()
    {
        // illegal char replacement
        {
            "A:B/C*D",
            new ProviderConstraints { IllegalNameChars = new[] { ':', '*' } },
            "A_B/C_D"
        },
        // reserved-name suffixing (case-insensitive)
        {
            "inbox/Sub",
            new ProviderConstraints { ReservedFolderNames = new[] { "Inbox" } },
            "inbox_/Sub"
        },
        // path-length truncation of last segment
        {
            "AAAA/BBBBBBBBBB",
            new ProviderConstraints { MaxPathLengthChars = 8 }, // "AAAA/" = 5, leaves 3 for last seg
            "AAAA/BBB"
        },
        // permissive defaults -> unchanged
        {
            "Projects/2026/Q1",
            new ProviderConstraints(),
            "Projects/2026/Q1"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sanitize_TransformsPerConstraints(string input, ProviderConstraints c, string expected)
    {
        FolderSanitizer.Sanitize(FolderPath.Parse(input), c).ToString().Should().Be(expected);
    }

    [Fact]
    public void Sanitize_DoesNotMutateInput()
    {
        var input = FolderPath.Parse("A:B");
        var c = new ProviderConstraints { IllegalNameChars = new[] { ':' } };
        var _ = FolderSanitizer.Sanitize(input, c);
        input.ToString().Should().Be("A:B");
    }

    [Fact]
    public void Sanitize_TrimsResultingWhitespace()
    {
        // illegal char ' ' replaced first would change semantics; here we just verify trimming
        var c = new ProviderConstraints { IllegalNameChars = new[] { '*' } };
        FolderSanitizer.Sanitize(FolderPath.Parse(" A* "), c).ToString().Should().Be("A_");
    }
}
