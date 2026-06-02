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
        => throw new NotSupportedException("Graph source provider is implemented in a later task.");

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
        => throw new NotSupportedException("Graph destination provider is implemented in a later task.");
}
