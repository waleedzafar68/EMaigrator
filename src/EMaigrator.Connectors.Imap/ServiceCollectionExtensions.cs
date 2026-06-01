using System;
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Connectors.Imap;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the IMAP connector plugin (CONTRACTS §8 naming convention).</summary>
    public static IServiceCollection AddImapConnector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderPlugin, ImapProviderPlugin>());
        return services;
    }
}
