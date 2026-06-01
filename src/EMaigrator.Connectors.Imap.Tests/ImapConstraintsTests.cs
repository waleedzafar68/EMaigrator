using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using FluentAssertions;
using MailKit.Security;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapConstraintsTests
{
    [Fact]
    public void Default_constraints_have_no_hard_depth_and_known_separator()
    {
        var c = ImapConstraints.Default('/');
        c.FolderSeparator.Should().Be('/');
        c.MaxFolderDepth.Should().Be(int.MaxValue);
        c.IllegalNameChars.Should().Contain('/');
    }

    [Fact]
    public void Build_oauth2_mechanism_uses_xoauth2()
    {
        var mech = ImapClientFactory.BuildOAuth2Mechanism("u@corp.example", "ya29.token");
        mech.Should().BeOfType<SaslMechanismOAuth2>();
        mech.MechanismName.Should().Be("XOAUTH2");
    }

    [Fact]
    public void Require_secret_returns_present_value()
    {
        var values = new Dictionary<string, string> { ["password"] = "p@ss" };
        ImapClientFactory.RequireSecret(values, "password").Should().Be("p@ss");
    }

    [Fact]
    public void Require_secret_missing_throws_without_leaking_other_values()
    {
        var values = new Dictionary<string, string> { ["password"] = "p@ss" };
        var act = () => ImapClientFactory.RequireSecret(values, "accessToken");
        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().NotContain("p@ss");
    }
}
