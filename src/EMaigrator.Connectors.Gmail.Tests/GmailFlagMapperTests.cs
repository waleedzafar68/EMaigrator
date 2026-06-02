using System.Collections.Generic;
using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailFlagMapperTests
{
    [Fact]
    public void NoUnreadLabel_MeansSeen()
    {
        var flags = GmailFlagMapper.ToFlags(new[] { "INBOX" });
        flags.Should().HaveFlag(MessageFlags.Seen);
    }

    [Fact]
    public void UnreadLabel_MeansNotSeen()
    {
        var flags = GmailFlagMapper.ToFlags(new[] { "INBOX", "UNREAD" });
        flags.Should().NotHaveFlag(MessageFlags.Seen);
    }

    [Fact]
    public void StarredAndDraft_MapToFlaggedAndDraft()
    {
        var flags = GmailFlagMapper.ToFlags(new[] { "STARRED", "DRAFT", "UNREAD" });
        flags.Should().HaveFlag(MessageFlags.Flagged);
        flags.Should().HaveFlag(MessageFlags.Draft);
        flags.Should().NotHaveFlag(MessageFlags.Seen);
    }

    [Fact]
    public void ToCanonicalLabels_ReturnsOnlyUserLabelNames()
    {
        var idToName = new Dictionary<string, string>
        {
            ["INBOX"] = "INBOX",
            ["UNREAD"] = "UNREAD",
            ["CATEGORY_PROMOTIONS"] = "CATEGORY_PROMOTIONS",
            ["Label_42"] = "Work/Clients/Acme",
        };

        var labels = GmailFlagMapper.ToCanonicalLabels(
            new[] { "INBOX", "UNREAD", "CATEGORY_PROMOTIONS", "Label_42" }, idToName);

        labels.Should().Equal(new[] { "Work/Clients/Acme" });
    }
}
