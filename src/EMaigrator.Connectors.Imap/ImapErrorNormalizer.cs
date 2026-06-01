using System.Net.Sockets;
using MailKit; // ServiceNotConnectedException / ServiceNotAuthenticatedException
using MailKit.Net.Imap;
using MailKit.Security;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Maps transport-layer exceptions to a stable, credential-free errorSignature
/// string ("imap:&lt;condition&gt;") for the Core error catalog (CONTRACTS §8). The
/// signature is derived only from exception TYPE and well-known protocol response
/// codes — never from free-form message text — so a credential embedded in a
/// message can never leak into the signature.
/// </summary>
public static class ImapErrorNormalizer
{
    public static string Normalize(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        switch (ex)
        {
            case OperationCanceledException:
                throw ex; // cancellation is control flow, not an error signature
            case AuthenticationException:
                return "imap:auth-failed";
            case SslHandshakeException:
                return "imap:tls-handshake-failed";
            case ImapCommandException cmd:
                return cmd.Response switch
                {
                    ImapCommandResponse.No => "imap:command-no",
                    ImapCommandResponse.Bad => "imap:command-bad",
                    _ => "imap:command-ok",
                };
            case ImapProtocolException:
                return "imap:protocol-error";
            case SocketException:
                return "imap:connect-failed";
            case ServiceNotConnectedException:
                return "imap:not-connected";
            case ServiceNotAuthenticatedException:
                return "imap:not-authenticated";
            case TimeoutException:
                return "imap:timeout";
            default:
                return "imap:unknown";
        }
    }
}
