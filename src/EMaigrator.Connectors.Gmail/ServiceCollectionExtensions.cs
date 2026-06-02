using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Connectors.Gmail;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Gmail connector's single <see cref="IProviderPlugin"/> for DI discovery.</summary>
    public static IServiceCollection AddGmailConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderPlugin, GmailProviderPlugin>());
        return services;
    }
}
