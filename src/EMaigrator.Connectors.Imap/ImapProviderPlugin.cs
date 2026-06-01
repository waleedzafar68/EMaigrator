using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// DI-discovered IMAP plugin (CONTRACTS §2). One per connector assembly.
/// </summary>
public sealed class ImapProviderPlugin : IProviderPlugin
{
    private readonly ILoggerFactory _loggerFactory;

    public ImapProviderPlugin() : this(NullLoggerFactory.Instance) { }
    public ImapProviderPlugin(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public ProviderId Id => new("imap");

    public IReadOnlyCollection<AuthMethod> SupportedAuth { get; } =
        new[] { AuthMethod.ImapBasic, AuthMethod.ImapOAuthXoauth2 };

    public bool CanBeSource => true;
    public bool CanBeDestination => true;

    public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets)
        => new ImapSourceProvider(descriptor, secrets, _loggerFactory.CreateLogger<ImapSourceProvider>());

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets)
        => new ImapDestinationProvider(descriptor, secrets, _loggerFactory.CreateLogger<ImapDestinationProvider>());
}
