using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Connectors.Graph;

public static class GraphConnectorServiceCollectionExtensions
{
    /// <summary>Registers the Microsoft Graph connector plugin for DI discovery.</summary>
    public static IServiceCollection AddGraphConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderPlugin, GraphProviderPlugin>());
        return services;
    }
}
