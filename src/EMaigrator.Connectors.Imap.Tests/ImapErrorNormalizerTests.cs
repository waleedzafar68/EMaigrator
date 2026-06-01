using System.Net.Sockets;
using EMaigrator.Connectors.Imap;
using FluentAssertions;
using MailKit.Net.Imap;
using MailKit.Security;
using Xunit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapErrorNormalizerTests
{
    [Fact]
    public void Auth_exception_maps_to_auth_failed()
    {
        var sig = ImapErrorNormalizer.Normalize(new AuthenticationException("bad login"));
        sig.Should().Be("imap:auth-failed");
    }

    [Fact]
    public void Ssl_handshake_maps_to_tls_handshake_failed()
    {
        var sig = ImapErrorNormalizer.Normalize(new SslHandshakeException("tls"));
        sig.Should().Be("imap:tls-handshake-failed");
    }

    [Fact]
    public void Socket_exception_maps_to_connect_failed()
    {
        var sig = ImapErrorNormalizer.Normalize(new SocketException(10061));
        sig.Should().Be("imap:connect-failed");
    }

    [Fact]
    public void Command_exception_maps_to_command_response()
    {
        // MailKit 4.16.0 exposes the public ctor (ImapCommandResponse response,
        // string responseText). It is the unambiguous public ctor that sets
        // Response; this is all the normalizer reads.
        var ex = new ImapCommandException(ImapCommandResponse.No, "mailbox does not exist");
        ex.Response.Should().Be(ImapCommandResponse.No);

        var sig = ImapErrorNormalizer.Normalize(ex);
        sig.Should().Be("imap:command-no");
    }

    [Fact]
    public void Cancellation_is_rethrown_not_normalized()
    {
        var act = () => ImapErrorNormalizer.Normalize(new OperationCanceledException());
        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Signature_never_contains_credential_substring()
    {
        const string password = "Sup3rSecretAppPassw0rd!";
        var ex = new AuthenticationException($"LOGIN failed for password {password}");
        var sig = ImapErrorNormalizer.Normalize(ex);
        sig.Should().NotContain(password);
    }

    [Fact]
    public void Unknown_exception_maps_to_unknown()
    {
        var sig = ImapErrorNormalizer.Normalize(new InvalidOperationException("weird"));
        sig.Should().Be("imap:unknown");
    }
}
