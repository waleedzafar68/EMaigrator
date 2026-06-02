using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// DI-discovered descriptor for the Gmail connector (CONTRACTS §2). v1 supports BYO
/// service-account + domain-wide delegation only (DESIGN.md §11). Each factory call builds a
/// fresh <see cref="GmailService"/> so the returned provider owns (and disposes) its own
/// service — source and destination never share a client.
/// </summary>
public sealed class GmailProviderPlugin : IProviderPlugin
{
    public static readonly ProviderId GmailProviderId = new("gmail");

    public ProviderId Id => GmailProviderId;

    public IReadOnlyCollection<AuthMethod> SupportedAuth { get; } =
        [AuthMethod.GmailServiceAccountDwd];

    public bool CanBeSource => true;

    public bool CanBeDestination => true;

    public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(secrets);

        var config = GmailConnectionConfig.FromDescriptor(descriptor, secrets);
        var service = GmailServiceFactory.Create(config);
        return new GmailSourceProvider(service, config.DelegatedUser);
    }

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(secrets);

        var config = GmailConnectionConfig.FromDescriptor(descriptor, secrets);
        var service = GmailServiceFactory.Create(config);
        return new GmailDestinationProvider(service, config.DelegatedUser);
    }
}
