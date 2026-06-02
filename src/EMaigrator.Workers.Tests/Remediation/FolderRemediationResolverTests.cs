using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Remediation;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Workers.Tests.Remediation;

public sealed class FolderRemediationResolverTests
{
    private static readonly ProviderConstraints Outlook = new()
    {
        MaxFolderDepth = 3,
        IllegalNameChars = new[] { '\\', ':' }
    };

    [Fact]
    public void No_remediation_returns_source_unchanged()
    {
        var src = FolderPath.Parse("Inbox/Clients");
        var dest = FolderRemediationResolver.Resolve(src, new List<ApprovedRemediation>(), Outlook);
        dest.ToString().Should().Be("Inbox/Clients");
    }

    [Fact]
    public void Flatten_action_collapses_to_max_depth()
    {
        var src = FolderPath.Parse("A/B/C/D/E");
        var approved = new List<ApprovedRemediation>
        {
            new("A/B/C/D/E", RemediationAction.FlattenFolder)
        };
        var dest = FolderRemediationResolver.Resolve(src, approved, Outlook);
        dest.Depth.Should().BeLessThanOrEqualTo(Outlook.MaxFolderDepth);
        dest.ToString().Should().Be(FolderFlattener.Flatten(src, Outlook.MaxFolderDepth).ToString());
    }

    [Fact]
    public void Sanitize_action_strips_illegal_chars()
    {
        var src = FolderPath.Parse(@"Inbox/Cli:ents");
        var approved = new List<ApprovedRemediation>
        {
            new(@"Inbox/Cli:ents", RemediationAction.SanitizeFolderName)
        };
        var dest = FolderRemediationResolver.Resolve(src, approved, Outlook);
        dest.ToString().Should().NotContain(":");
        dest.ToString().Should().Be(FolderSanitizer.Sanitize(src, Outlook).ToString());
    }

    [Fact]
    public void Remediation_matches_only_the_named_folder()
    {
        var src = FolderPath.Parse("Inbox/Other");
        var approved = new List<ApprovedRemediation>
        {
            new("A/B/C/D/E", RemediationAction.FlattenFolder)
        };
        var dest = FolderRemediationResolver.Resolve(src, approved, Outlook);
        dest.ToString().Should().Be("Inbox/Other");
    }
}
