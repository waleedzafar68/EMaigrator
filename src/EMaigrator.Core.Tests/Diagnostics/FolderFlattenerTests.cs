using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Tests.Diagnostics;

public class FolderFlattenerTests
{
    public static TheoryData<string, int, char, string> Cases() => new()
    {
        { "A/B/C/D/E", 1, '-', "A-B-C-D-E" },
        { "A/B/C/D/E", 3, '-', "A/B/C-D-E" },
        { "A/B", 3, '-', "A/B" },           // already within depth -> unchanged
        { "A/B/C", 3, '-', "A/B/C" },       // exactly at depth -> unchanged
        { "A/B/C/D", 2, '_', "A/B_C_D" },   // custom join char '_' collapses tail beyond depth 2
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Flatten_CollapsesBeyondMaxDepth(string input, int maxDepth, char join, string expected)
    {
        FolderFlattener.Flatten(FolderPath.Parse(input), maxDepth, join).ToString().Should().Be(expected);
    }

    [Fact]
    public void Flatten_HonorsCustomJoinChar()
    {
        FolderFlattener.Flatten(FolderPath.Parse("A/B/C/D"), 2, '_').ToString().Should().Be("A/B_C_D");
    }

    [Fact]
    public void Flatten_ThrowsWhenMaxDepthNonPositive()
    {
        var act = () => FolderFlattener.Flatten(FolderPath.Parse("A/B"), 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Flatten_IsDeterministic_CollisionIsCallerConcern()
    {
        // Two different deep trees CAN flatten to colliding names; the flattener itself is pure &
        // deterministic. Collision resolution belongs to the sanitizer/dedup caller, not here.
        var a = FolderFlattener.Flatten(FolderPath.Parse("A/B/C"), 1).ToString();
        var b = FolderFlattener.Flatten(FolderPath.Parse("A/B/C"), 1).ToString();
        a.Should().Be(b).And.Be("A-B-C");
    }

    [Fact]
    public void Flatten_DoesNotMutateInput()
    {
        var input = FolderPath.Parse("A/B/C/D");
        var _ = FolderFlattener.Flatten(input, 1);
        input.ToString().Should().Be("A/B/C/D");
    }
}
