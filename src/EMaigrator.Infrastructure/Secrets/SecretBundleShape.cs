using System.Collections.Generic;
using System.Text.Json;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// Single source of truth mapping an <see cref="AuthMethod"/> to the flat JSON secret blob the matching
/// connector expects in its <see cref="SecretBundle"/>. The blob is stored verbatim via
/// <see cref="ISecretStore"/>; EVERY read path — the API connect-test, the API preflight, the worker run
/// (<c>ProviderSessionFactory</c>), and the CLI — MUST resolve it through <see cref="Unwrap"/> so
/// connect-test, preflight, and run all agree. Storing the wrong key passes fake-backed tests but fails
/// the real run with an auth error (see CONTRACTS §4 + docs/connectors/authoring-a-connector.md §4).
/// </summary>
public static class SecretBundleShape
{
    /// <summary>The connector-specific key a raw credential is stored under for the given auth method.</summary>
    public static string KeyFor(AuthMethod auth) => auth switch
    {
        AuthMethod.ImapBasic => "password",
        AuthMethod.ImapOAuthXoauth2 => "accessToken",
        AuthMethod.GraphAppOAuth or AuthMethod.GraphDelegatedOAuth => "clientSecret",
        AuthMethod.GmailServiceAccountDwd => "serviceAccountJson",
        _ => "password",
    };

    /// <summary>
    /// Wraps a raw credential into the connector-shaped JSON. For <see cref="AuthMethod.GmailServiceAccountDwd"/>,
    /// <paramref name="raw"/> is the entire service-account key-file contents (itself a JSON object), stored as a
    /// string value under the "serviceAccountJson" key the Gmail connector reads.
    /// </summary>
    public static string Wrap(AuthMethod auth, string raw) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { [KeyFor(auth)] = raw });

    /// <summary>
    /// Deserializes a stored connector-shaped blob back into the flat secret dictionary. A null/blank/garbage
    /// blob resolves to an empty bundle rather than throwing, so a missing-secret connection degrades to the
    /// connector's own "required secret missing" error instead of an unhandled exception.
    /// </summary>
    public static Dictionary<string, string> Unwrap(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
