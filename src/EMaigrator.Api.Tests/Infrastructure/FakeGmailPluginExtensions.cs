using System;
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Appends a deterministic <c>gmail</c> <see cref="IProviderPlugin"/> to the test host so the provider
/// catalog endpoint (<c>GET /providers</c>) sees all three v1 connectors (imap + graph are already
/// registered by <c>AddTestPlugins</c> / <c>AddFakePreflight</c>). The fake mirrors the real
/// <c>GmailProviderPlugin</c>'s capabilities (service-account domain-wide delegation only) so the
/// endpoint's <c>canBatch</c> derivation resolves to <c>true</c> for gmail. It is APPENDED (no
/// <c>RemoveAll</c>) and never built into a connector, so it is harmless to every other suite.
/// </summary>
public static class FakeGmailPluginExtensions
{
    public static void AddFakeGmailPlugin(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IProviderPlugin, FakeGmailPlugin>();
    }

    private sealed class FakeGmailPlugin : IProviderPlugin
    {
        public ProviderId Id => new("gmail");

        public IReadOnlyCollection<AuthMethod> SupportedAuth => new[] { AuthMethod.GmailServiceAccountDwd };

        public bool CanBeSource => true;

        public bool CanBeDestination => true;

        public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets) =>
            throw new NotSupportedException("FakeGmailPlugin is a catalog-only double.");

        public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets) =>
            throw new NotSupportedException("FakeGmailPlugin is a catalog-only double.");
    }
}
