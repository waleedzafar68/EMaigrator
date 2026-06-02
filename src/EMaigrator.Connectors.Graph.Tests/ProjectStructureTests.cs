using System.Reflection;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Connectors.Graph.Tests;

public class ProjectStructureTests
{
    [Fact]
    public void Assembly_references_Core_but_not_infrastructure_layers()
    {
        var referenced = typeof(GraphProviderPlugin).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenced.Should().Contain("EMaigrator.Core");
        referenced.Should().NotContain("EMaigrator.Infrastructure");
        referenced.Should().NotContain("EMaigrator.Workers");
        referenced.Should().NotContain("EMaigrator.Api");
        referenced.Should().NotContain("EMaigrator.Cli");
    }

    [Fact]
    public void Dependency_rule_holds_no_higher_layer_references()
    {
        var referenced = typeof(GraphProviderPlugin).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        // DESIGN §15: a connector depends only on EMaigrator.Core (plus framework/SDK packages).
        referenced.Should().NotContain(new[]
        {
            "EMaigrator.Infrastructure",
            "EMaigrator.Workers",
            "EMaigrator.Api",
            "EMaigrator.Cli",
        });
    }

    [Fact]
    public void AddGraphConnector_registers_the_plugin_as_IProviderPlugin()
    {
        var services = new ServiceCollection();
        services.AddGraphConnector();

        var provider = services.BuildServiceProvider();
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        plugin.Should().BeOfType<GraphProviderPlugin>();
        plugin.Id.Value.Should().Be("graph");
    }
}
