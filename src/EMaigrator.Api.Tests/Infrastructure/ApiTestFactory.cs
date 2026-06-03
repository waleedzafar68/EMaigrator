using System;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

        return base.CreateHost(builder);
    }
}
