namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Thrown when a <c>ConnectionDescriptor</c> cannot be turned into valid IMAP
/// settings (unknown region, disallowed host, plaintext without opt-in).
/// Messages MUST NOT contain credentials or secret refs beyond the offending
/// non-secret value.
/// </summary>
public sealed class ImapConfigurationException : Exception
{
    public ImapConfigurationException(string message) : base(message) { }
}
