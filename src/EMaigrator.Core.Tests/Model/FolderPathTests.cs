using EMaigrator.Core.Model;

namespace EMaigrator.Core.Tests.Model;

public class FolderPathTests
{
    [Fact]
    public void Parse_SplitsOnDefaultSeparator()
    {
        var p = FolderPath.Parse("Inbox/Projects/2026");
        p.Segments.Should().Equal("Inbox", "Projects", "2026");
        p.Depth.Should().Be(3);
        p.Name.Should().Be("2026");
        p.IsRoot.Should().BeFalse();
    }

    [Fact]
    public void Parse_HonorsCustomSeparatorAndTrimsEmpties()
    {
        var p = FolderPath.Parse("|A||B|", '|');
        p.Segments.Should().Equal("A", "B");
    }

    [Fact]
    public void Root_IsEmpty()
    {
        var root = FolderPath.Parse("");
        root.IsRoot.Should().BeTrue();
        root.Depth.Should().Be(0);
        root.Name.Should().Be("");
    }

    [Fact]
    public void ToString_JoinsWithSeparator()
    {
        var p = new FolderPath(new[] { "A", "B", "C" });
        p.ToString().Should().Be("A/B/C");
        p.ToString('\\').Should().Be("A\\B\\C");
    }

    [Fact]
    public void Parent_DropsLastSegment()
    {
        var p = FolderPath.Parse("A/B/C");
        p.Parent().Should().Be(FolderPath.Parse("A/B"));
    }

    [Fact]
    public void Parent_OnRoot_Throws()
    {
        var act = () => FolderPath.Parse("").Parent();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        FolderPath.Parse("A/B").Should().Be(new FolderPath(new[] { "A", "B" }));
        FolderPath.Parse("A/B").Should().NotBe(FolderPath.Parse("A/C"));
    }

    [Fact]
    public void Constructor_StoresDefensiveCopy()
    {
        var src = new List<string> { "A", "B" };
        var p = new FolderPath(src);
        src.Add("C");
        p.Segments.Should().Equal("A", "B");
    }
}
