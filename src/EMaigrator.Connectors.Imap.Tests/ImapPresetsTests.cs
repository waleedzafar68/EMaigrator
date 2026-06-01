using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapPresetsTests
{
    private static ConnectionDescriptor Descriptor(IReadOnlyDictionary<string, string> settings)
        => new()
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = settings,
            SecretRef = "secret/abc",
        };

    [Theory]
    [InlineData("us-east-1", "imap.mail.us-east-1.awsapps.com")]
    [InlineData("us-west-2", "imap.mail.us-west-2.awsapps.com")]
    [InlineData("eu-west-1", "imap.mail.eu-west-1.awsapps.com")]
    public void Resolves_workmail_region_preset_to_host_993_ssl(string region, string expectedHost)
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "workmail",
            ["region"] = region,
            ["accountEmail"] = "user@corp.example",
        });

        var settings = ImapPresets.Resolve(d);

        settings.Host.Should().Be(expectedHost);
        settings.Port.Should().Be(993);
        settings.UseSsl.Should().BeTrue();
        settings.AccountEmail.Should().Be("user@corp.example");
    }

    [Fact]
    public void Unknown_workmail_region_throws_naming_region_without_secret()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "workmail",
            ["region"] = "mars-east-9",
            ["accountEmail"] = "user@corp.example",
        });

        var act = () => ImapPresets.Resolve(d);

        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain("mars-east-9").And.NotContain("secret/abc");
    }

    [Fact]
    public void Custom_server_defaults_to_993_ssl()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "imap.zoho.com",
            ["accountEmail"] = "user@zoho.example",
        });

        var settings = ImapPresets.Resolve(d);

        settings.Host.Should().Be("imap.zoho.com");
        settings.Port.Should().Be(993);
        settings.UseSsl.Should().BeTrue();
    }

    [Fact]
    public void Custom_server_honors_explicit_host_and_port()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "mail.example.org",
            ["port"] = "1993",
            ["accountEmail"] = "u@example.org",
        });

        var settings = ImapPresets.Resolve(d);

        settings.Host.Should().Be("mail.example.org");
        settings.Port.Should().Be(1993);
        settings.UseSsl.Should().BeTrue();
    }

    [Fact]
    public void Plaintext_without_explicit_optin_is_rejected()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "mail.example.org",
            ["port"] = "143",
            ["useSsl"] = "false",
            ["accountEmail"] = "u@example.org",
        });

        var act = () => ImapPresets.Resolve(d);

        act.Should().Throw<ImapConfigurationException>()
            .Which.Message.Should().Contain("TLS");
    }

    [Fact]
    public void Plaintext_with_explicit_optin_is_allowed()
    {
        var d = Descriptor(new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "mail.example.org",
            ["port"] = "143",
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = "u@example.org",
        });

        var settings = ImapPresets.Resolve(d);

        settings.UseSsl.Should().BeFalse();
        settings.Port.Should().Be(143);
    }
}
