using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Model;
using FluentAssertions;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphFolderMapperTests
{
    private static GraphFolderWellKnown WellKnown() => new(
        InboxId: "inbox-id",
        DraftsId: "drafts-id",
        SentItemsId: "sent-id",
        DeletedItemsId: "deleted-id");

    [Fact]
    public void Maps_well_known_root_and_nested_child_to_canonical_paths()
    {
        var nodes = new[]
        {
            new GraphMailFolderNode("inbox-id", "Inbox", null, 10),
            new GraphMailFolderNode("b-id", "Projects", "inbox-id", 3),
            new GraphMailFolderNode("c-id", "2026", "b-id", 1)
        };

        var folders = GraphFolderMapper.BuildTree(nodes, WellKnown());

        var paths = folders.Select(f => f.Path.ToString()).ToArray();
        paths.Should().Contain("Inbox");
        paths.Should().Contain("Inbox/Projects");
        paths.Should().Contain("Inbox/Projects/2026");

        folders.Single(f => f.Path.ToString() == "Inbox/Projects")
               .EstimatedMessageCount.Should().Be(3);
    }

    [Fact]
    public void Drafts_well_known_folder_carries_draft_special_use()
    {
        var nodes = new[] { new GraphMailFolderNode("drafts-id", "Drafts", null, 0) };

        var folders = GraphFolderMapper.BuildTree(nodes, WellKnown());

        folders.Single().SpecialUse.Should().Be(MessageFlags.Draft);
    }

    [Fact]
    public void BuildTree_is_order_independent()
    {
        var ordered = new[]
        {
            new GraphMailFolderNode("inbox-id", "Inbox", null, 0),
            new GraphMailFolderNode("b-id", "Projects", "inbox-id", 0)
        };
        var shuffled = ordered.Reverse().ToArray();

        var a = GraphFolderMapper.BuildTree(ordered, WellKnown()).Select(f => f.Path.ToString()).OrderBy(x => x);
        var b = GraphFolderMapper.BuildTree(shuffled, WellKnown()).Select(f => f.Path.ToString()).OrderBy(x => x);

        a.Should().Equal(b);
    }

    [Fact]
    public void Orphan_node_with_unknown_parent_is_skipped()
    {
        var nodes = new[]
        {
            new GraphMailFolderNode("x-id", "Orphan", "missing-parent", 5)
        };

        var folders = GraphFolderMapper.BuildTree(nodes, WellKnown());

        folders.Should().BeEmpty();
    }

    [Fact]
    public void ResolveFolderId_returns_id_for_existing_path_else_null()
    {
        var nodes = new[]
        {
            new GraphMailFolderNode("inbox-id", "Inbox", null, 0),
            new GraphMailFolderNode("b-id", "Projects", "inbox-id", 0)
        };
        var idsByPath = GraphFolderMapper.BuildIdIndex(nodes, WellKnown());

        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Inbox/Projects"), idsByPath).Should().Be("b-id");
        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Inbox/Nope"), idsByPath).Should().BeNull();
    }
}
