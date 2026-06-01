namespace EMaigrator.Connectors.Imap;

/// <summary>
/// A transport failure already normalized to a credential-free
/// <c>errorSignature</c>. The <see cref="Signature"/> is safe to log and to feed
/// the Core error catalog.
/// </summary>
public sealed class ImapTransportException : Exception
{
    public string Signature { get; }
    public ImapTransportException(string signature) : base(signature) => Signature = signature;
}
