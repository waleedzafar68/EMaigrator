using System.Globalization;
using System.IO;

namespace EMaigrator.Cli.IntegrationTests;

/// <summary>
/// Writes self-host migration-profile JSON files for the CLI e2e tests. Notes on the exact shape:
/// the IMAP connector reads <c>useSsl</c> (NOT <c>useTls</c>); <c>ImapHostValidator</c> refuses a
/// loopback host unless <c>allowPlaintext=true</c>; <c>preset:custom</c> selects the host/port; and
/// <c>ProfileLoader</c> rejects any settings key containing a secret fragment
/// (password/secret/token/apikey/key/credential) — every key here is clean. All settings VALUES are
/// strings (port is quoted) because <c>ConnectionProfile.Settings</c> is a string-to-string map.
/// </summary>
public static class CliProfiles
{
    public static string WriteImapToImap(string dir, int imapPort) =>
        WriteImapToImap(dir, imapPort, "source@greenmail.local", "dest@greenmail.local");

    /// <summary>Writes the IMAP→IMAP profile for an EXPLICIT source/dest account pair, so each e2e test
    /// class can run against its own dedicated GreenMail mailboxes (no cross-class destination contention).</summary>
    public static string WriteImapToImap(string dir, int imapPort, string sourceUser, string destUser)
    {
        var path = Path.Combine(dir, "profile.json");
        var port = imapPort.ToString(CultureInfo.InvariantCulture);
        File.WriteAllText(path, $$"""
        {
          "tenantId": "self-host",
          "storeSubjects": false,
          "from": { "provider": "imap", "auth": "ImapBasic",
            "settings": { "preset": "custom", "host": "127.0.0.1", "port": "{{port}}",
                          "useSsl": "false", "allowPlaintext": "true", "accountEmail": "{{sourceUser}}" } },
          "to":   { "provider": "imap", "auth": "ImapBasic",
            "settings": { "preset": "custom", "host": "127.0.0.1", "port": "{{port}}",
                          "useSsl": "false", "allowPlaintext": "true", "accountEmail": "{{destUser}}" } },
          "scope": { "isBatch": false,
            "pairs": [ { "sourceMailbox": "{{sourceUser}}", "destMailbox": "{{destUser}}" } ] }
        }
        """);
        return path;
    }
}
