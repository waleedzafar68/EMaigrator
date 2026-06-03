using EMaigrator.Cli.Profile;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Secrets;

public enum MigrationSide { From, To }

/// <summary>
/// Obtains a side's secret from env or a no-echo prompt (NEVER a CLI arg),
/// stores it via <see cref="ISecretStore"/>, and returns the opaque SecretRef.
/// The plaintext never leaves this method.
/// </summary>
public sealed class SecretResolver(ISecretStore secretStore, IConsoleSecretReader reader)
{
    public async Task<string> ResolveAsync(
        MigrationSide side, ConnectionProfile connection, string tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        string envVar = side == MigrationSide.From ? "EMAIGRATOR_SECRET_FROM" : "EMAIGRATOR_SECRET_TO";
        string? raw = Environment.GetEnvironmentVariable(envVar);

        if (string.IsNullOrEmpty(raw))
        {
            string label = $"Secret for {side} ({connection.Provider}/{connection.Auth})";
            raw = reader.ReadSecret(label);
        }

        // Service-account auth: the env/prompt value is a *path*; store the file contents.
        string plaintext = connection.Auth == AuthMethod.GmailServiceAccountDwd && File.Exists(raw)
            ? await File.ReadAllTextAsync(raw, ct)
            : raw;

        string blob = SecretBundleShape.ForAuth(connection.Auth, plaintext);
        return await secretStore.StoreAsync(tenantId, blob, ct);
    }
}
