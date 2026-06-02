using Azure.Identity;
using Microsoft.Graph;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Builds a <see cref="GraphServiceClient"/> for the client-credentials flow. The token cache is
/// kept in-memory only — <see cref="ClientSecretCredentialOptions.TokenCachePersistenceOptions"/>
/// is left null so no token is ever written to disk in plaintext (INDEX security focus; DESIGN.md §10).
/// </summary>
public static class GraphClientFactory
{
    /// <summary>The only scope ever requested: the app's pre-consented application permissions (.default).</summary>
    public static readonly string[] GraphScopes = GraphConnectionConfig.GraphScopes;

    /// <summary>
    /// Credential options used for every Graph credential. <see cref="ClientSecretCredentialOptions.TokenCachePersistenceOptions"/>
    /// is intentionally left null so the acquired token stays in process memory only and is never
    /// persisted to disk.
    /// </summary>
    public static ClientSecretCredentialOptions BuildCredentialOptions() => new()
    {
        // Intentionally NOT setting TokenCachePersistenceOptions: the token stays in memory only.
        TokenCachePersistenceOptions = null,
    };

    public static GraphServiceClient Build(GraphConnectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var credential = new ClientSecretCredential(
            config.TenantId, config.ClientId, config.ClientSecret, BuildCredentialOptions());
        return new GraphServiceClient(credential, GraphScopes);
    }
}
