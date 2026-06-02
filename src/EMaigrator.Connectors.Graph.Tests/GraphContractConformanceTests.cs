using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphContractConformanceTests
{
    private static ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("graph"),
        Auth = AuthMethod.GraphAppOAuth,
        Settings = new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            ["clientId"] = "22222222-2222-2222-2222-222222222222",
            ["accountEmail"] = "a@contoso.com",
        },
        SecretRef = "ref",
    };

    private static SecretBundle Bundle() =>
        new(new Dictionary<string, string> { ["clientSecret"] = "s" });

    private static IProviderPlugin ResolvePlugin()
    {
        var services = new ServiceCollection();
        services.AddGraphConnector();
        return services.BuildServiceProvider().GetServices<IProviderPlugin>()
            .Single(p => p.Id.Value == "graph");
    }

    [Fact]
    public void Plugin_is_discoverable_via_DI()
    {
        ResolvePlugin().Should().BeOfType<GraphProviderPlugin>();
    }

    [Fact]
    public async Task Source_implements_full_contract_and_disposes()
    {
        ISourceProvider source = ResolvePlugin().CreateSource(Descriptor(), Bundle());

        source.Id.Value.Should().Be("graph");
        source.Constraints.Should().BeSameAs(GraphConstraints.MS365);
        source.Should().BeAssignableTo<IAsyncDisposable>();

        await source.DisposeAsync(); // must not throw
    }

    [Fact]
    public async Task Destination_implements_full_contract_and_disposes()
    {
        IDestinationProvider dest = ResolvePlugin().CreateDestination(Descriptor(), Bundle());

        dest.Id.Value.Should().Be("graph");
        dest.Constraints.Should().BeSameAs(GraphConstraints.MS365);
        dest.Should().BeAssignableTo<IAsyncDisposable>();

        await dest.DisposeAsync();
    }

    [Fact]
    public void Concrete_types_declare_all_contract_methods()
    {
        var sourceMethods = typeof(GraphSourceProvider).GetMethods().Select(m => m.Name).ToArray();
        sourceMethods.Should().Contain(nameof(ISourceProvider.TestConnectionAsync));
        sourceMethods.Should().Contain(nameof(ISourceProvider.ListFoldersAsync));
        sourceMethods.Should().Contain(nameof(ISourceProvider.ReadMessagesAsync));

        var destMethods = typeof(GraphDestinationProvider).GetMethods().Select(m => m.Name).ToArray();
        destMethods.Should().Contain(nameof(IDestinationProvider.TestConnectionAsync));
        destMethods.Should().Contain(nameof(IDestinationProvider.EnsureFolderAsync));
        destMethods.Should().Contain(nameof(IDestinationProvider.WriteMessageAsync));
        destMethods.Should().Contain(nameof(IDestinationProvider.ExistsByMessageIdAsync));
    }
}
