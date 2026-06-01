using System;
using System.Net;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Anti-SSRF guard. A test-connection or migration may only connect to the host
/// that the provider preset prescribes (WorkMail) or that the operator explicitly
/// declared (custom). Blocks loopback/link-local/cloud-metadata literals unless an
/// explicit plaintext opt-in is present (self-host / test escape hatch).
/// </summary>
public static class ImapHostValidator
{
    private static readonly char[] ForbiddenHostChars = { '/', '@', '\\', ' ', '\t', '\r', '\n', '?', '#' };

    public static void Validate(ConnectionDescriptor descriptor, string resolvedHost)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(resolvedHost))
            throw new ImapConfigurationException("Resolved IMAP host is empty.");

        if (resolvedHost.IndexOfAny(ForbiddenHostChars) >= 0 || resolvedHost.Contains("//", StringComparison.Ordinal))
            throw new ImapConfigurationException(
                $"Refusing to connect to malformed host '{resolvedHost}'.");

        var s = descriptor.Settings;
        var preset = s.TryGetValue("preset", out var p) ? p : "custom";

        if (string.Equals(preset, "workmail", StringComparison.OrdinalIgnoreCase))
        {
            var region = s.TryGetValue("region", out var r) ? r : null;
            if (region is null || !ImapPresets.WorkMailRegions.TryGetValue(region, out var expected))
                throw new ImapConfigurationException(
                    $"WorkMail region '{region}' is not a known IMAP region.");
            if (!string.Equals(resolvedHost, expected, StringComparison.OrdinalIgnoreCase))
                throw new ImapConfigurationException(
                    $"Refusing to connect to '{resolvedHost}': WorkMail preset only permits '{expected}'.");
            return;
        }

        // custom: host must match what the operator declared (no silent rewrite)
        var declared = s.TryGetValue("host", out var h) ? h : null;
        if (declared is null || !string.Equals(resolvedHost, declared, StringComparison.OrdinalIgnoreCase))
            throw new ImapConfigurationException(
                $"Refusing to connect to '{resolvedHost}': it does not match the declared host '{declared}'.");

        var allowPlaintext = s.TryGetValue("allowPlaintext", out var ap) && bool.TryParse(ap, out var b) && b;
        if (!allowPlaintext && IsBlockedLiteral(resolvedHost))
            throw new ImapConfigurationException(
                $"Refusing to connect to internal/metadata address '{resolvedHost}'.");
    }

    private static bool IsBlockedLiteral(string host)
    {
        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            return false; // a DNS name; not a literal we block here
        if (IPAddress.IsLoopback(ip)) return true;
        var bytes = ip.GetAddressBytes();
        // IPv4 link-local 169.254.0.0/16 (includes 169.254.169.254 metadata)
        if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) return true;
        // IPv6 link-local fe80::/10
        if (bytes.Length == 16 && bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
        return false;
    }
}
