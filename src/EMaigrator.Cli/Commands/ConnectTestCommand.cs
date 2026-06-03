using System.Text.Json;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class ConnectTestCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        MigrationProfile profile, MigrationSide side,
        IReadOnlyList<IProviderPlugin> plugins, SecretResolver secretResolver,
        ISecretStore secretStore, IOutputWriter writer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(secretResolver);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(writer);

        ConnectionProfile conn = side == MigrationSide.From ? profile.From : profile.To;
        IProviderPlugin? plugin = plugins.FirstOrDefault(p => p.Id.Equals(conn.Provider));
        if (plugin is null)
        {
            writer.WriteError($"No connector plugin registered for provider '{conn.Provider}'.");
            return CliExitCode.ConfigError;
        }

        string secretRef = await secretResolver.ResolveAsync(side, conn, profile.TenantId, ct);
        try
        {
            string stored = await secretStore.RetrieveAsync(secretRef, ct);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stored)
                         ?? new Dictionary<string, string>();
            var bundle = new SecretBundle(values);
            ConnectionDescriptor descriptor = ConnectionBuilder.BuildDescriptor(conn, secretRef);

            ConnectionTestResult result = side == MigrationSide.From
                ? await ExecSource(plugin, descriptor, bundle, ct)
                : await ExecDest(plugin, descriptor, bundle, ct);

            writer.WriteConnectTest(new ConnectTestOutput(
                result.Ok, result.FolderCount, result.MessageCount, result.ErrorCode));

            return result.Ok ? CliExitCode.Success : CliExitCode.ConnectionFailed;
        }
        finally
        {
            // connect-test is not a runnable job → leave no standing secret.
            await secretStore.PurgeAsync(secretRef, ct);
        }
    }

    private static async Task<ConnectionTestResult> ExecSource(
        IProviderPlugin plugin, ConnectionDescriptor d, SecretBundle b, CancellationToken ct)
    {
        await using ISourceProvider source = plugin.CreateSource(d, b);
        return await source.TestConnectionAsync(ct);
    }

    private static async Task<ConnectionTestResult> ExecDest(
        IProviderPlugin plugin, ConnectionDescriptor d, SecretBundle b, CancellationToken ct)
    {
        await using IDestinationProvider dest = plugin.CreateDestination(d, b);
        return await dest.TestConnectionAsync(ct);
    }
}
