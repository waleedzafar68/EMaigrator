namespace EMaigrator.Core.Abstractions;

/// <summary>Per-provider auth methods supported in v1 (CONTRACTS.md §2).</summary>
public enum AuthMethod
{
    ImapBasic,
    ImapOAuthXoauth2,
    GraphAppOAuth,
    GraphDelegatedOAuth,
    GmailServiceAccountDwd,
    GmailDelegatedOAuth,
}
