using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapFolderMapperTests
{
    [Fact]
    public void Slash_delimited_server_name_maps_to_segments()
    {
        var fp = ImapFolderMapper.ToFolderPath("INBOX/Projects/2026", '/');
        fp.Segments.Should().Equal("INBOX", "Projects", "2026");
    }

    [Fact]
    public void Dot_delimited_server_name_maps_to_segments()
    {
        var fp = ImapFolderMapper.ToFolderPath("INBOX.Projects.2026", '.');
        fp.Segments.Should().Equal("INBOX", "Projects", "2026");
    }

    [Fact]
    public void Folder_path_maps_back_to_dot_server_name()
    {
        var fp = new FolderPath(new[] { "INBOX", "Projects" });
        ImapFolderMapper.ToServerName(fp, '.').Should().Be("INBOX.Projects");
    }

    [Fact]
    public void Root_maps_to_empty_server_name()
    {
        var fp = new FolderPath(System.Array.Empty<string>());
        ImapFolderMapper.ToServerName(fp, '.').Should().Be("");
    }

    [Fact]
    public void Segment_containing_other_delimiter_is_preserved()
    {
        var fp = new FolderPath(new[] { "A/B" });
        ImapFolderMapper.ToServerName(fp, '.').Should().Be("A/B");
    }
}
