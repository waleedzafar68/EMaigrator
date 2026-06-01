using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Preflight;

namespace EMaigrator.Core.Tests.Preflight;

public class PreflightTypesTests
{
    [Fact]
    public void ScopeSpec_Defaults()
    {
        var s = new ScopeSpec();
        s.IsBatch.Should().BeFalse();
        s.Pairs.Should().BeEmpty();
        s.IncludeFolders.Should().BeNull();
        s.ExcludeFolders.Should().BeNull();
        s.Since.Should().BeNull();
        s.Before.Should().BeNull();
    }

    [Fact]
    public void MailboxPair_Constructs()
    {
        var p = new MailboxPair("a@old.com", "a@new.com");
        p.SourceMailbox.Should().Be("a@old.com");
        p.DestMailbox.Should().Be("a@new.com");
    }

    [Fact]
    public void PreflightIssue_Constructs()
    {
        var issue = new PreflightIssue(
            "FolderTooDeep",
            new[] { "A/B/C/D/E" },
            RemediationAction.FlattenFolder,
            new[] { RemediationAction.FlattenFolder, RemediationAction.RenameFolder },
            Severity.Warning,
            "Folder exceeds destination max depth.");
        issue.IssueType.Should().Be("FolderTooDeep");
        issue.AffectedPaths.Should().ContainSingle();
        issue.RecommendedAction.Should().Be(RemediationAction.FlattenFolder);
    }

    [Fact]
    public void PreflightPlan_WrapsIssuesAndEstimate()
    {
        var estimate = new MigrationEstimate(1, 5, 1000, 50_000_000, TimeSpan.FromMinutes(10));
        var plan = new PreflightPlan(Array.Empty<PreflightIssue>(), estimate);
        plan.Issues.Should().BeEmpty();
        plan.Estimate.MessageCount.Should().Be(1000);
        plan.Estimate.EstimatedDuration.Should().Be(TimeSpan.FromMinutes(10));
    }
}
