using EMaigrator.Cli.Profile;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class ConnectionBuilder
{
    public static ConnectionDescriptor BuildDescriptor(ConnectionProfile profile, string secretRef)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new()
        {
            Provider = profile.Provider,
            Auth = profile.Auth,
            Settings = profile.Settings,
            SecretRef = secretRef,
        };
    }
}
