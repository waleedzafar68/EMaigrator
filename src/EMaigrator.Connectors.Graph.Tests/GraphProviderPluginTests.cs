using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphProviderPluginTests
{
    private static ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("graph"),
        Auth = AuthMethod.GraphAppOAuth,
        Settings = new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            ["clientId"] = "22222222-2222-2222-2222-222222222222",
            ["accountEmail"] = "user@contoso.onmicrosoft.com",
        },
        SecretRef = "ref",
    };

    private static SecretBundle Bundle() =>
        new(new Dictionary<string, string> { ["clientSecret"] = "the-secret" });

    [Fact]
    public void Plugin_advertises_capabilities()
    {
        var plugin = new GraphProviderPlugin();
        plugin.Id.Value.Should().Be("graph");
        plugin.SupportedAuth.Should().Contain(AuthMethod.GraphAppOAuth);
        plugin.SupportedAuth.Should().Contain(AuthMethod.GraphDelegatedOAuth);
        plugin.CanBeSource.Should().BeTrue();
        plugin.CanBeDestination.Should().BeTrue();
    }

    [Fact]
    public void CreateSource_returns_graph_source_provider()
    {
        var source = new GraphProviderPlugin().CreateSource(Descriptor(), Bundle());
        source.Should().BeOfType<GraphSourceProvider>();
        source.Id.Value.Should().Be("graph");
    }

    [Fact]
    public void CreateDestination_returns_graph_destination_provider()
    {
        var dest = new GraphProviderPlugin().CreateDestination(Descriptor(), Bundle());
        dest.Should().BeOfType<GraphDestinationProvider>();
    }

    [Fact]
    public void CreateSource_throws_config_exception_when_secret_missing()
    {
        var empty = new SecretBundle(new Dictionary<string, string>());
        var act = () => new GraphProviderPlugin().CreateSource(Descriptor(), empty);
        act.Should().Throw<GraphConfigurationException>();
    }

    [Fact]
    public void CreateDestination_throws_config_exception_when_secret_missing()
    {
        var empty = new SecretBundle(new Dictionary<string, string>());
        var act = () => new GraphProviderPlugin().CreateDestination(Descriptor(), empty);
        act.Should().Throw<GraphConfigurationException>();
    }

    [Fact]
    public void Credential_options_do_not_persist_token_cache_to_disk()
    {
        var options = GraphClientFactory.BuildCredentialOptions();
        options.TokenCachePersistenceOptions.Should().BeNull();
    }

    [Fact]
    public void Factory_uses_least_privilege_scope()
    {
        GraphClientFactory.GraphScopes.Should().Equal("https://graph.microsoft.com/.default");
    }
}
