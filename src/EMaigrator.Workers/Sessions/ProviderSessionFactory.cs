using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Secrets;

namespace EMaigrator.Workers.Sessions;

public sealed class ProviderSessionFactory : IProviderSessionFactory
{
    private readonly IReadOnlyList<IProviderPlugin> _plugins;
    private readonly ISecretStore _secrets;
    private readonly IMigrationConnectionLookup _lookup;

    public ProviderSessionFactory(
        IEnumerable<IProviderPlugin> plugins,
        ISecretStore secrets,
        IMigrationConnectionLookup lookup)
    {
        _plugins = plugins.ToList();
        _secrets = secrets;
        _lookup = lookup;
    }

    public async Task<ISourceProvider> CreateSourceAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        var conns = await _lookup.GetAsync(mailboxMigrationId, ct);
        var plugin = Plugin(conns.Source.Provider);
        var bundle = await ResolveSecretsAsync(conns.Source, ct);
        return plugin.CreateSource(conns.Source, bundle);
    }

    public async Task<IDestinationProvider> CreateDestinationAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        var conns = await _lookup.GetAsync(mailboxMigrationId, ct);
        var plugin = Plugin(conns.Dest.Provider);
        var bundle = await ResolveSecretsAsync(conns.Dest, ct);
        return plugin.CreateDestination(conns.Dest, bundle);
    }

    private IProviderPlugin Plugin(ProviderId id)
        => _plugins.FirstOrDefault(p => p.Id.Value == id.Value)
           ?? throw new InvalidOperationException($"No provider plugin registered for '{id.Value}'.");

    private async Task<SecretBundle> ResolveSecretsAsync(ConnectionDescriptor descriptor, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(descriptor.SecretRef))
            return new SecretBundle(new Dictionary<string, string>());

        // Transient plaintext — never logged (DESIGN.md §10). Resolved via the shared SecretBundleShape so
        // the run path, the API connect-test/preflight, and the CLI all read the same connector key.
        var plaintext = await _secrets.RetrieveAsync(descriptor.SecretRef, ct);
        return new SecretBundle(SecretBundleShape.Unwrap(plaintext));
    }
}
