using System.Collections.Generic;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Builds an authenticated <see cref="GmailService"/> using a BYO service account with
/// domain-wide delegation. Scope is intentionally the single broad-but-necessary
/// "https://mail.google.com/" (the only scope that authorizes raw RFC822 read AND
/// messages.import/insert with internalDate). The SA JSON is parsed in-memory only.
/// </summary>
public static class GmailServiceFactory
{
    /// <summary>
    /// Least-privilege scope set. https://mail.google.com/ is required because Gmail's
    /// readonly scope cannot fetch format=raw with full fidelity, and import/insert require
    /// full mail access; no narrower scope authorizes both directions. Justification recorded
    /// in the Security Verification task.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredScopes = new[] { "https://mail.google.com/" };

    public const string ApplicationName = "EMaigrator";

    public static GmailService Create(GmailConnectionConfig config)
    {
        var credential = GoogleCredential
            .FromJson(config.ServiceAccountJson)
            .CreateScoped(RequiredScopes)
            .CreateWithUser(config.DelegatedUser); // domain-wide delegation impersonation

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
    }

    /// <summary>Overload allowing a pre-built service (used by HTTP-fixture tests).</summary>
    public static GmailService Create(BaseClientService.Initializer initializer)
        => new GmailService(initializer);
}
