namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Resolved, validated IMAP endpoint settings. Contains no secret material —
/// the password / OAuth token lives only in the transient <c>SecretBundle</c>.
/// </summary>
public sealed record ImapConnectionSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool UseSsl { get; init; }
    public required string AccountEmail { get; init; }
}
