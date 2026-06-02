using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Connectors.Gmail.Tests;

public class GmailProviderPluginTests
{
    private static ConnectionDescriptor Descriptor(string? email = "target@example.com") => new()
    {
        Provider = new ProviderId("gmail"),
        Auth = AuthMethod.GmailServiceAccountDwd,
        Settings = new Dictionary<string, string> { ["accountEmail"] = email ?? "" },
    };

    private static SecretBundle Secrets() =>
        new(new Dictionary<string, string> { ["serviceAccountJson"] = TestServiceAccount.Json });

    [Fact]
    public void Metadata_DeclaresGmailDwdSourceAndDestination()
    {
        var plugin = new GmailProviderPlugin();
        plugin.Id.Should().Be(new ProviderId("gmail"));
        plugin.SupportedAuth.Should().Equal(new[] { AuthMethod.GmailServiceAccountDwd });
        plugin.CanBeSource.Should().BeTrue();
        plugin.CanBeDestination.Should().BeTrue();
    }

    [Fact]
    public void CreateSource_ReturnsGmailSourceProvider()
    {
        var plugin = new GmailProviderPlugin();
        var src = plugin.CreateSource(Descriptor(), Secrets());
        src.Should().BeOfType<GmailSourceProvider>();
        src.Id.Should().Be(new ProviderId("gmail"));
    }

    [Fact]
    public void CreateDestination_ReturnsGmailDestinationProvider()
    {
        var plugin = new GmailProviderPlugin();
        var dst = plugin.CreateDestination(Descriptor(), Secrets());
        dst.Should().BeOfType<GmailDestinationProvider>();
    }

    [Fact]
    public void CreateSource_MissingEmail_Throws()
    {
        var plugin = new GmailProviderPlugin();
        var act = () => plugin.CreateSource(Descriptor(email: ""), Secrets());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddGmailConnector_RegistersSinglePlugin()
    {
        var sp = new ServiceCollection().AddGmailConnector().BuildServiceProvider();
        var plugins = sp.GetServices<IProviderPlugin>().ToList();
        plugins.Should().ContainSingle(p => p.Id == new ProviderId("gmail"));
    }
}
