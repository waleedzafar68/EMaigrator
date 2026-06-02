using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Parsed, validated Graph connection parameters. Non-secret values come from
/// <see cref="ConnectionDescriptor.Settings"/>; the client secret comes from the
/// transient <see cref="SecretBundle"/>. Least-privilege: only the application's
/// pre-consented Mail.ReadWrite permission is exercised via the .default scope.
/// </summary>
public sealed class GraphConnectionConfig
{
    /// <summary>The only scope ever requested: the app's pre-consented application permissions.</summary>
    public static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string AccountEmail { get; init; }
    public required string ClientSecret { get; init; }

    public static GraphConnectionConfig FromDescriptor(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(secrets);

        if (descriptor.Auth is not (AuthMethod.GraphAppOAuth or AuthMethod.GraphDelegatedOAuth))
        {
            throw new GraphConfigurationException(
                $"Graph connector does not support auth method '{descriptor.Auth}'. " +
                "Expected GraphAppOAuth or GraphDelegatedOAuth.");
        }

        var tenantId = RequireSetting(descriptor, "tenantId");
        var clientId = RequireSetting(descriptor, "clientId");
        var accountEmail = RequireSetting(descriptor, "accountEmail");

        if (!secrets.Values.TryGetValue("clientSecret", out var clientSecret)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new GraphConfigurationException(
                "Required secret 'clientSecret' is missing from the secret bundle.");
        }

        return new GraphConnectionConfig
        {
            TenantId = tenantId,
            ClientId = clientId,
            AccountEmail = accountEmail,
            ClientSecret = clientSecret,
        };
    }

    private static string RequireSetting(ConnectionDescriptor descriptor, string key)
    {
        if (!descriptor.Settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new GraphConfigurationException($"Required connection setting '{key}' is missing or empty.");
        }

        return value;
    }

    // Redacted: never include the client secret in diagnostic output.
    public override string ToString() =>
        $"GraphConnectionConfig(tenant={TenantId}, client={ClientId}, account={AccountEmail}, secret=***REDACTED***)";
}
