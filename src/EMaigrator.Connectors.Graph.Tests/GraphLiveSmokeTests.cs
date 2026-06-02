using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>
/// Gated live smoke against the free M365 Developer Program tenant (DESIGN §17). These facts run
/// ONLY when all EMAIGRATOR_GRAPH_* environment variables are set; otherwise they are skipped, so
/// the default per-commit/CI run never makes a live Graph call. Excluded from coverage %.
/// </summary>
public class GraphLiveSmokeTests
{
    private static (bool Ready, ConnectionDescriptor Descriptor, SecretBundle Secrets) Env()
    {
        var tenant = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_CLIENT_SECRET");
        var account = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_ACCOUNT_EMAIL");

        var ready = !(string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(account));

        var descriptor = new ConnectionDescriptor
        {
            Provider = new ProviderId("graph"),
            Auth = AuthMethod.GraphAppOAuth,
            Settings = new Dictionary<string, string>
            {
                ["tenantId"] = tenant ?? string.Empty,
                ["clientId"] = clientId ?? string.Empty,
                ["accountEmail"] = account ?? string.Empty,
            },
            SecretRef = "live",
        };
        var secrets = new SecretBundle(new Dictionary<string, string> { ["clientSecret"] = secret ?? string.Empty });
        return (ready, descriptor, secrets);
    }

    [SkippableFact]
    public async Task TestConnection_against_live_developer_tenant()
    {
        var (ready, descriptor, secrets) = Env();
        Skip.IfNot(ready, "Set EMAIGRATOR_GRAPH_* env vars to run the live smoke test.");

        await using var source = new GraphProviderPlugin().CreateSource(descriptor, secrets);
        var result = await source.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.FolderCount.Should().BeGreaterThan(0);
    }
}
