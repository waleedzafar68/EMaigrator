using System.Text.Json;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Secrets;

/// <summary>
/// Single source of truth mapping an AuthMethod to the flat JSON secret blob that the matching
/// connector expects in its SecretBundle (the worker's ProviderSessionFactory deserializes the
/// stored blob with JsonSerializer.Deserialize&lt;Dictionary&lt;string,string&gt;&gt;). Keeping this in one
/// place guarantees connect-test, preflight, and the worker-run path all agree.
/// </summary>
public static class SecretBundleShape
{
    /// <summary>
    /// Wraps a raw credential into the connector-shaped JSON. For GmailServiceAccountDwd, <paramref name="raw"/>
    /// is the entire service-account key-file CONTENTS (already a flat JSON object of strings), stored under
    /// the "serviceAccountJson" key the Gmail connector reads.
    /// </summary>
    public static string ForAuth(AuthMethod auth, string raw)
    {
        string key = auth switch
        {
            AuthMethod.ImapBasic => "password",
            AuthMethod.ImapOAuthXoauth2 => "accessToken",
            AuthMethod.GraphAppOAuth or AuthMethod.GraphDelegatedOAuth => "clientSecret",
            AuthMethod.GmailServiceAccountDwd => "serviceAccountJson",
            _ => "password",
        };
        return JsonSerializer.Serialize(new Dictionary<string, string> { [key] = raw });
    }
}
