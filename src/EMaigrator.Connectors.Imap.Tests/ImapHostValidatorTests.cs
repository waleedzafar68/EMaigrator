using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapHostValidatorTests
{
    private static ConnectionDescriptor Workmail(string region) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "workmail",
            ["region"] = region,
            ["accountEmail"] = "u@corp.example",
        },
    };

    private static ConnectionDescriptor Custom(string host, bool allowPlaintext = false) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = host,
            ["accountEmail"] = "u@example.org",
            ["allowPlaintext"] = allowPlaintext ? "true" : "false",
            ["useSsl"] = allowPlaintext ? "false" : "true",
        },
    };

    [Fact]
    public void Workmail_allows_only_the_canonical_region_host()
    {
        var d = Workmail("eu-west-1");
        var act = () => ImapHostValidator.Validate(d, "imap.mail.eu-west-1.awsapps.com");
        act.Should().NotThrow();
    }

    [Fact]
    public void Workmail_rejects_host_that_is_not_the_region_template()
    {
        var d = Workmail("eu-west-1");
        var act = () => ImapHostValidator.Validate(d, "evil.internal.corp");
        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain("evil.internal.corp");
    }

    [Fact]
    public void Custom_allows_declared_host()
    {
        var d = Custom("imap.zoho.com");
        var act = () => ImapHostValidator.Validate(d, "imap.zoho.com");
        act.Should().NotThrow();
    }

    [Fact]
    public void Custom_rejects_host_other_than_declared()
    {
        var d = Custom("imap.zoho.com");
        var act = () => ImapHostValidator.Validate(d, "169.254.169.254");
        act.Should().Throw<ImapConfigurationException>();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("169.254.169.254")]
    public void Custom_rejects_metadata_and_loopback_without_optin(string host)
    {
        var d = Custom(host);
        var act = () => ImapHostValidator.Validate(d, host);
        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain(host);
    }

    [Fact]
    public void Custom_allows_loopback_with_explicit_plaintext_optin()
    {
        var d = Custom("127.0.0.1", allowPlaintext: true);
        var act = () => ImapHostValidator.Validate(d, "127.0.0.1");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("imap.zoho.com/evil")]
    [InlineData("user@imap.zoho.com")]
    [InlineData("imap.zoho.com ")]
    public void Rejects_hosts_with_scheme_path_or_credential_characters(string host)
    {
        var d = Custom(host);
        var act = () => ImapHostValidator.Validate(d, host);
        act.Should().Throw<ImapConfigurationException>();
    }
}
