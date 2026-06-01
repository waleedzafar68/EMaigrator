using EMaigrator.Core.Model;

namespace EMaigrator.Core.Abstractions;

/// <summary>DI-discovered plugin descriptor — one per connector assembly (CONTRACTS.md §2).</summary>
public interface IProviderPlugin
{
    ProviderId Id { get; }
    IReadOnlyCollection<AuthMethod> SupportedAuth { get; }
    bool CanBeSource { get; }
    bool CanBeDestination { get; }
    ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets);
    IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets);
}
