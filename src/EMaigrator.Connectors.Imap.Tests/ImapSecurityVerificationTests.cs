using System.Collections.Generic;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

/// <summary>
/// Pure-unit half of the security gate: TLS-enforcement and anti-SSRF invariants
/// that must hold WITHOUT a live server, plus the credential-free error signature.
/// The live half (credential-never-logged against a real auth failure) is in
/// <c>ImapSecurityLiveTests</c> in the integration-test project.
/// </summary>
public class ImapSecurityVerificationTests
{
    [Fact]
    public void Tls_is_enforced_when_plaintext_not_opted_in()
    {
        var d = new ConnectionDescriptor
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string>
            {
                ["preset"] = "custom",
                ["host"] = "mail.example.org",
                ["port"] = "143",
                ["useSsl"] = "false",
                ["accountEmail"] = "u@example.org",
            },
        };

        var act = () => ImapPresets.Resolve(d);
        act.Should().Throw<ImapConfigurationException>().Which.Message.Should().Contain("TLS");
    }

    [Fact]
    public void Workmail_preset_ignores_planted_host_and_resolves_to_region_host()
    {
        var d = new ConnectionDescriptor
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string>
            {
                ["preset"] = "workmail",
                ["region"] = "eu-west-1",
                ["host"] = "169.254.169.254", // planted; must be ignored for presets
                ["accountEmail"] = "u@corp.example",
            },
        };

        var settings = ImapPresets.Resolve(d);
        settings.Host.Should().Be("imap.mail.eu-west-1.awsapps.com");

        // And the validator forbids dialing anything but the preset host.
        var act = () => ImapHostValidator.Validate(d, "169.254.169.254");
        act.Should().Throw<ImapConfigurationException>();
    }

    [Fact]
    public void Error_normalizer_signature_carries_no_credential()
    {
        const string pw = "Sup3rSecret-PW-XYZ";
        var ex = new MailKit.Security.AuthenticationException($"login failed for {pw}");
        var sig = ImapErrorNormalizer.Normalize(ex);
        sig.Should().Be("imap:auth-failed");
        sig.Should().NotContain(pw);
    }
}
