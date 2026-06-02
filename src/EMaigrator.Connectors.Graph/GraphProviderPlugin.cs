using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Graph;

public sealed class GraphProviderPlugin : IProviderPlugin
{
    public static readonly ProviderId GraphProviderId = new("graph");

    public ProviderId Id => GraphProviderId;

    public IReadOnlyCollection<AuthMethod> SupportedAuth { get; } =
        [AuthMethod.GraphAppOAuth, AuthMethod.GraphDelegatedOAuth];

    public bool CanBeSource => true;

    public bool CanBeDestination => true;

    public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(secrets);

        var config = GraphConnectionConfig.FromDescriptor(descriptor, secrets);
        var client = GraphClientFactory.Build(config);
        return new GraphSourceProvider(client, config.AccountEmail);
    }

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(secrets);

        var config = GraphConnectionConfig.FromDescriptor(descriptor, secrets);
        var client = GraphClientFactory.Build(config);
        return new GraphDestinationProvider(client, config.AccountEmail);
    }
}
