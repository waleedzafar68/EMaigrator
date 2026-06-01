using System.Collections.Generic;
using System.Linq;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapProviderPluginTests
{
    private static ConnectionDescriptor BasicDescriptor() => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = "imap.example.org",
            ["accountEmail"] = "u@example.org",
        },
        SecretRef = "secret/x",
    };

    private static SecretBundle Secret() =>
        new(new Dictionary<string, string> { ["password"] = "p@ss" });

    [Fact]
    public void Plugin_advertises_imap_identity_and_capabilities()
    {
        var plugin = new ImapProviderPlugin();
        plugin.Id.Should().Be(new ProviderId("imap"));
        plugin.SupportedAuth.Should().BeEquivalentTo(new[] { AuthMethod.ImapBasic, AuthMethod.ImapOAuthXoauth2 });
        plugin.CanBeSource.Should().BeTrue();
        plugin.CanBeDestination.Should().BeTrue();
    }

    [Fact]
    public void Create_source_returns_imap_source_provider()
    {
        var plugin = new ImapProviderPlugin();
        var src = plugin.CreateSource(BasicDescriptor(), Secret());
        src.Should().BeAssignableTo<ISourceProvider>();
        src.Id.Should().Be(new ProviderId("imap"));
        src.Constraints.FolderSeparator.Should().Be('/');
    }

    [Fact]
    public void Create_destination_returns_imap_destination_provider()
    {
        var plugin = new ImapProviderPlugin();
        var dst = plugin.CreateDestination(BasicDescriptor(), Secret());
        dst.Should().BeAssignableTo<IDestinationProvider>();
        dst.Id.Should().Be(new ProviderId("imap"));
    }

    [Fact]
    public void AddImapConnector_registers_single_plugin()
    {
        var services = new ServiceCollection();
        services.AddImapConnector();
        var provider = services.BuildServiceProvider();
        var plugins = provider.GetServices<IProviderPlugin>().ToList();
        plugins.Should().ContainSingle().Which.Should().BeOfType<ImapProviderPlugin>();
    }
}
