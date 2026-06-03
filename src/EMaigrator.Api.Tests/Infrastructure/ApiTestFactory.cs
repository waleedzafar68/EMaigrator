using System;
using EMaigrator.Api.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// In-process API server bound to the live containers from <see cref="ApiInfraFixture"/>. Injects the
/// fixture's <c>Infrastructure:*</c> configuration (connection strings, LocalKey secret store, default
/// rate bucket) so the real composition root resolves and <c>/health</c> reports every backend up. The
/// schema is already migrated by the fixture, so the factory does not migrate again.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly ApiInfraFixture _fixture;

    public ApiTestFactory(ApiInfraFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _fixture = fixture;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The composition root snapshots Infrastructure:* connection strings into the health checks at
        // registration time (inside WebApplication.CreateBuilder → AddEMaigratorApi → AddInfrastructure).
        // So the container values must already be on the host's configuration BEFORE that snapshot runs.
        // ConfigureHostConfiguration contributes to the earliest configuration layer, ahead of the app's
        // appsettings.json placeholders and ahead of AddInfrastructure's health-check snapshot.
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(_fixture.ConfigurationValues()));

        // Override the per-request ICurrentTenant with a test accessor whose tenant can be set
        // explicitly for direct-DbContext seeding scopes (and which still reads the tenant_id claim
        // for real HTTP requests). Scoped so each request/seeding scope gets its own instance.
        //
        // The factory ALWAYS registers the connection-test doubles here too: AddTestPlugins REPLACES the
        // real connector plugins + IErrorCatalog with a deterministic FakeImapPlugin + NSubstitute
        // catalog, so the connection-test path never reaches a real IMAP server. This is harmless for the
        // earlier tests (they don't resolve plugins). Later tasks add more doubles to this same spot.
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<ICurrentTenant, TestCurrentTenant>());
            FakeImapPluginFactoryExtensions.AddTestPlugins(services);
        });

        return base.CreateHost(builder);
    }
}
