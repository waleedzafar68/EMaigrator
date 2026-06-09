using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Secrets;

/// <summary>
/// Thin CLI-facing alias over the canonical <see cref="EMaigrator.Infrastructure.Secrets.SecretBundleShape"/>
/// (the single source of truth shared with the API connect-test/preflight and the worker run path). Kept as
/// a named entry point so existing CLI call-sites/tests are unchanged while the AuthMethod→key mapping lives
/// in exactly one place.
/// </summary>
public static class SecretBundleShape
{
    /// <summary>
    /// Wraps a raw credential into the connector-shaped JSON. For GmailServiceAccountDwd, <paramref name="raw"/>
    /// is the entire service-account key-file CONTENTS (already a flat JSON object of strings), stored under
    /// the "serviceAccountJson" key the Gmail connector reads.
    /// </summary>
    public static string ForAuth(AuthMethod auth, string raw) =>
        EMaigrator.Infrastructure.Secrets.SecretBundleShape.Wrap(auth, raw);
}
