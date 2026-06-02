using EMaigrator.Connectors.Gmail;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailConstraintsTests
{
    [Fact]
    public void Default_HasGmailSpecificLimits()
    {
        var c = GmailConstraints.Default;

        c.FolderSeparator.Should().Be('/');
        c.MaxMessageBytes.Should().Be(35L * 1024 * 1024);
        c.MaxAttachmentBytes.Should().Be(25L * 1024 * 1024);
        c.MaxFolderDepth.Should().Be(int.MaxValue);
        c.IllegalNameChars.Should().Contain('/');
    }

    [Fact]
    public void Default_ReservesSystemLabelNames()
    {
        var c = GmailConstraints.Default;

        c.ReservedFolderNames.Should().Contain(new[]
        {
            "INBOX", "SENT", "DRAFT", "SPAM", "TRASH",
            "STARRED", "IMPORTANT", "UNREAD", "CHAT", "CATEGORY_PERSONAL",
        });
    }
}
