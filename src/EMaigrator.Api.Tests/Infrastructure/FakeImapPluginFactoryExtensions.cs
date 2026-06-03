using System;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Test doubles for the connection-test path. <see cref="ApiTestFactory"/> ALWAYS calls
/// <see cref="AddTestPlugins"/> from its service-configuration (the same place the test
/// <c>ICurrentTenant</c> is registered), so the With* markers here just return the factory unchanged —
/// they document intent at the call site. <see cref="AddTestPlugins"/> is deterministic: it REMOVES the
/// real connector plugins + any real <see cref="IErrorCatalog"/> registered by the production
/// composition root, then registers the fake plugin + an NSubstitute catalog, so a test never reaches a
/// real IMAP server or depends on the production catalog's rule set.
/// </summary>
public static class FakeImapPluginFactoryExtensions
{
    public static ApiTestFactory WithFakeImapPlugin(this ApiTestFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory;
    }

    public static void AddTestPlugins(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Replace the real connector plugins (TryAddEnumerable'd by AddImapConnector/AddGraphConnector/
        // AddGmailConnector in production DI) so the deterministic fake is the only IProviderPlugin and
        // the service's "imap" lookup resolves to it.
        services.RemoveAll<IProviderPlugin>();
        services.AddSingleton<IProviderPlugin, FakeImapPlugin>();

        // Replace the real catalog with a substitute that maps the connector's normalized auth-failure
        // signature to a resolution; the service derives the stable "IMAP_AUTH_FAILED" code from the signature.
        services.RemoveAll<IErrorCatalog>();
        var catalog = Substitute.For<IErrorCatalog>();
        catalog.Match(Arg.Any<ProviderId>(), Arg.Is<string>(s => s.Contains("AUTHENTICATIONFAILED", StringComparison.Ordinal)))
            .Returns(new ErrorResolution(
                new ErrorRule
                {
                    SignatureRegex = "AUTHENTICATIONFAILED",
                    Diagnosis = "Auth failed",
                    Suggestion = "Use an app password",
                    Kind = RemediationKind.Structural,
                    Severity = Severity.Blocker,
                },
                "Auth failed",
                "Use an app password",
                RemediationKind.Structural,
                RemediationAction.None,
                Array.Empty<RemediationAction>(),
                Severity.Blocker));
        services.AddSingleton(catalog);
    }
}
