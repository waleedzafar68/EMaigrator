using EMaigrator.Connectors.Graph;
using FluentAssertions;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphConstraintsTests
{
    [Fact]
    public void MS365_declares_expected_caps()
    {
        var c = GraphConstraints.MS365;

        c.MaxFolderDepth.Should().Be(300);
        c.MaxMessageBytes.Should().Be(150L * 1024 * 1024);
        c.MaxAttachmentBytes.Should().Be(150L * 1024 * 1024);
        c.FolderSeparator.Should().Be('/');
    }

    [Theory]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData(':')]
    [InlineData('*')]
    [InlineData('?')]
    [InlineData('"')]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData('|')]
    public void MS365_declares_illegal_folder_characters(char illegal)
    {
        GraphConstraints.MS365.IllegalNameChars.Should().Contain(illegal);
    }

    [Theory]
    [InlineData("Inbox")]
    [InlineData("Sent Items")]
    [InlineData("Drafts")]
    [InlineData("Deleted Items")]
    [InlineData("Junk Email")]
    [InlineData("Archive")]
    [InlineData("Outbox")]
    public void MS365_reserves_well_known_folder_names(string reserved)
    {
        GraphConstraints.MS365.ReservedFolderNames.Should().Contain(reserved);
    }

    [Fact]
    public void MS365_is_a_singleton_instance()
    {
        GraphConstraints.MS365.Should().BeSameAs(GraphConstraints.MS365);
    }
}
