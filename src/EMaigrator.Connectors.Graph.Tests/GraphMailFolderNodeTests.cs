using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Graph.Models;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>
/// Regression cover for the live folder-resolution defect: Graph reports a top-level folder's
/// parentFolderId as the mailbox root's REAL id (the literal "msgfolderroot" is only a URL alias), and
/// never returns the root itself in the folder list. <see cref="GraphMailFolderNode.BuildFromGraph"/>
/// must therefore treat a parent that is absent from the fetched set as the root, or custom top-level
/// folders silently become orphans and can't be resolved or written to.
/// </summary>
public class GraphMailFolderNodeTests
{
    private static GraphFolderWellKnown WellKnown() => new(
        InboxId: "inbox-id", DraftsId: "drafts-id", SentItemsId: "sent-id", DeletedItemsId: "deleted-id");

    [Fact]
    public void Top_level_folder_with_real_root_id_resolves_as_a_root()
    {
        var folders = new[]
        {
            new MailFolder { Id = "custom-id", DisplayName = "Projects", ParentFolderId = "real-root-id", TotalItemCount = 3 },
            new MailFolder { Id = "child-id", DisplayName = "2026", ParentFolderId = "custom-id", TotalItemCount = 1 },
        };

        var idsByPath = GraphFolderMapper.BuildIdIndex(GraphMailFolderNode.BuildFromGraph(folders), WellKnown());

        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Projects"), idsByPath).Should().Be("custom-id");
        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Projects/2026"), idsByPath).Should().Be("child-id");
    }

    [Fact]
    public void Literal_msgfolderroot_and_empty_parent_are_roots()
    {
        var folders = new[]
        {
            new MailFolder { Id = "a", DisplayName = "Alpha", ParentFolderId = "msgfolderroot", TotalItemCount = 0 },
            new MailFolder { Id = "b", DisplayName = "Beta", ParentFolderId = null, TotalItemCount = 0 },
        };

        var idsByPath = GraphFolderMapper.BuildIdIndex(GraphMailFolderNode.BuildFromGraph(folders), WellKnown());

        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Alpha"), idsByPath).Should().Be("a");
        GraphFolderMapper.ResolveFolderId(FolderPath.Parse("Beta"), idsByPath).Should().Be("b");
    }

    [Fact]
    public void Nested_folder_whose_parent_is_in_the_set_keeps_its_parent()
    {
        var folders = new[]
        {
            new MailFolder { Id = "inbox-id", DisplayName = "Inbox", ParentFolderId = "real-root-id", TotalItemCount = 0 },
            new MailFolder { Id = "p", DisplayName = "Projects", ParentFolderId = "inbox-id", TotalItemCount = 0 },
        };

        var nodes = GraphMailFolderNode.BuildFromGraph(folders);

        nodes.Single(n => n.Id == "inbox-id").ParentFolderId.Should().BeNull("its parent is the unlisted mailbox root");
        nodes.Single(n => n.Id == "p").ParentFolderId.Should().Be("inbox-id", "its parent is a real fetched folder");
    }
}
