using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailLabelMapperTests
{
    [Theory]
    [InlineData("Work/Clients/Acme", new[] { "Work", "Clients", "Acme" })]
    [InlineData("INBOX", new[] { "INBOX" })]
    [InlineData("SENT", new[] { "SENT" })]
    public void LabelNameToFolderPath_SplitsNestedAndSystemLabels(string label, string[] expected)
    {
        var fp = GmailLabelMapper.LabelNameToFolderPath(label);
        fp.Segments.Should().Equal(expected);
    }

    [Theory]
    [InlineData(new[] { "Work", "Clients", "Acme" }, "Work/Clients/Acme")]
    [InlineData(new[] { "INBOX" }, "INBOX")]
    public void FolderPathToLabelName_JoinsWithSlash(string[] segments, string expected)
    {
        var fp = new FolderPath(segments);
        GmailLabelMapper.FolderPathToLabelName(fp).Should().Be(expected);
    }

    [Theory]
    [InlineData("INBOX", true)]
    [InlineData("SENT", true)]
    [InlineData("CATEGORY_PROMOTIONS", true)]
    [InlineData("Work", false)]
    [InlineData("Work/Clients", false)]
    public void IsSystemLabel_DetectsReservedNames(string label, bool expected)
        => GmailLabelMapper.IsSystemLabel(label).Should().Be(expected);

    [Theory]
    [InlineData("CHAT", false)]
    [InlineData("UNREAD", false)]
    [InlineData("INBOX", true)]
    [InlineData("SENT", true)]
    [InlineData("Work", true)]
    public void IsMappableLabel_ExcludesChatAndUnread(string label, bool expected)
        => GmailLabelMapper.IsMappableLabel(label).Should().Be(expected);

    [Fact]
    public void IsAllMail_OnlyTrueForSyntheticAllMailPath()
    {
        GmailLabelMapper.IsAllMail(FolderPath.Parse("[Gmail]/All Mail")).Should().BeTrue();
        GmailLabelMapper.IsAllMail(FolderPath.Parse("INBOX")).Should().BeFalse();
        GmailLabelMapper.IsAllMail(FolderPath.Parse("Work/All Mail")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("Work/Clients")]
    [InlineData("Receipts 2026")]
    public void RoundTrip_PreservesUserLabelNames(string name)
    {
        var fp = GmailLabelMapper.LabelNameToFolderPath(name);
        GmailLabelMapper.FolderPathToLabelName(fp).Should().Be(name);
    }
}
