using System;
using System.Collections.Generic;
using System.Globalization;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Imap;

public static class ImapPresets
{
    public static readonly IReadOnlyDictionary<string, string> WorkMailRegions =
        new Dictionary<string, string>
        {
            ["us-east-1"] = "imap.mail.us-east-1.awsapps.com",
            ["us-west-2"] = "imap.mail.us-west-2.awsapps.com",
            ["eu-west-1"] = "imap.mail.eu-west-1.awsapps.com",
        };

    public static ImapConnectionSettings Resolve(ConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var s = descriptor.Settings;
        var accountEmail = Get(s, "accountEmail")
            ?? throw new ImapConfigurationException("Missing required setting 'accountEmail'.");
        var preset = Get(s, "preset") ?? "custom";

        if (string.Equals(preset, "workmail", StringComparison.OrdinalIgnoreCase))
        {
            var region = Get(s, "region")
                ?? throw new ImapConfigurationException("WorkMail preset requires a 'region' setting.");
            if (!WorkMailRegions.TryGetValue(region, out var host))
            {
                throw new ImapConfigurationException(
                    $"Unknown WorkMail region '{region}'. Supported regions: " +
                    string.Join(", ", WorkMailRegions.Keys) + ".");
            }
            return new ImapConnectionSettings
            {
                Host = host, Port = 993, UseSsl = true, AccountEmail = accountEmail,
            };
        }

        var customHost = Get(s, "host")
            ?? throw new ImapConfigurationException("Custom IMAP server requires a 'host' setting.");
        var useSsl = ParseBool(Get(s, "useSsl"), defaultValue: true);
        var allowPlaintext = ParseBool(Get(s, "allowPlaintext"), defaultValue: false);
        var port = ParseInt(Get(s, "port"), defaultValue: useSsl ? 993 : 143);

        if (!useSsl && !allowPlaintext)
        {
            throw new ImapConfigurationException(
                "Refusing to connect without TLS. Set 'allowPlaintext=true' to explicitly opt in to an insecure connection.");
        }

        return new ImapConnectionSettings
        {
            Host = customHost, Port = port, UseSsl = useSsl, AccountEmail = accountEmail,
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string> s, string key)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static bool ParseBool(string? value, bool defaultValue)
        => value is null ? defaultValue : bool.Parse(value);

    private static int ParseInt(string? value, int defaultValue)
        => value is null ? defaultValue : int.Parse(value, CultureInfo.InvariantCulture);
}
