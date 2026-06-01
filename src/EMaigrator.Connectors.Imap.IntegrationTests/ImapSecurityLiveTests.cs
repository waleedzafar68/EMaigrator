using System.Globalization;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

/// <summary>Captures every log line + scope value for credential-leak assertions.</summary>
internal sealed class CapturingLogger : ILogger
{
    public List<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        Lines.Add("scope:" + state);
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Lines.Add(formatter(state, exception));
        if (exception is not null)
            Lines.Add(exception.ToString());
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Live half of the security gate, against a real GreenMail server with auth ENFORCED.
/// Proves: a real wrong-password failure surfaces the stable credential-free signature
/// with NO credential in any log line / exception message; TLS is enforced before any
/// socket opens; and an SSRF attempt at a metadata host is blocked before connecting.
/// </summary>
[Collection("greenmail")]
public class ImapSecurityLiveTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapSecurityLiveTests(GreenMailImapFixture fx) => _fx = fx;

    // Plaintext custom descriptor (useSsl=false). With allowPlaintext=true the
    // connection resolves and dials the test server; with allowPlaintext=false it is
    // rejected at Resolve (the TLS-enforcement path).
    private static ConnectionDescriptor CustomDescriptor(string host, int port, bool allowPlaintext = true) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = host,
            ["port"] = port.ToString(CultureInfo.InvariantCulture),
            ["useSsl"] = "false",
            ["allowPlaintext"] = allowPlaintext ? "true" : "false",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };

    // SSL custom descriptor (useSsl=true, no plaintext opt-in) — resolves successfully so
    // the anti-SSRF host validator (not the TLS check) is what blocks a metadata host.
    private static ConnectionDescriptor SslCustomDescriptor(string host, int port) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = host,
            ["port"] = port.ToString(CultureInfo.InvariantCulture),
            ["useSsl"] = "true",
            ["allowPlaintext"] = "false",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };

    [Fact]
    public async Task Wrong_password_yields_auth_failed_signature_with_no_credential_in_logs()
    {
        // Auth is ENFORCED on the fixture and migrator@local.test was seeded at the known
        // Password (GreenMailImapFixture.SeedUserAsync). A DIFFERENT password therefore
        // deterministically fails authentication.
        const string wrongPassword = "Sup3rSecret-PW-XYZ-WRONG";
        var logger = new CapturingLogger();
        var d = CustomDescriptor(_fx.Host, _fx.ImapPort);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = wrongPassword });

        await using var src = new ImapSourceProvider(d, secret, logger);
        var bad = await src.TestConnectionAsync(CancellationToken.None);

        // 1. The auth failure surfaces as the stable, credential-free signature.
        bad.Ok.Should().BeFalse();
        bad.ErrorCode.Should().Be("imap:auth-failed");
        // 2. No raw, credential-bearing detail is propagated (assertions run unconditionally).
        (bad.RawDetail ?? string.Empty).Should().NotContain(wrongPassword);
        bad.RawDetail.Should().BeNull();
        // 3. No captured log line — message, exception text, or scope — contains the secret.
        logger.Lines.Should().NotContain(l => l.Contains(wrongPassword, StringComparison.Ordinal));
        logger.Lines.Should().NotContain(l => l.Contains(GreenMailImapFixture.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Auth_failure_exception_message_carries_only_the_signature()
    {
        // Drives ImapClientFactory directly to assert the wrapped transport exception's
        // Message/Signature is EXACTLY the credential-free signature — never the raw MailKit
        // message that may echo the attempted credential.
        const string wrongPassword = "Another-Wrong-PW-9999";
        var logger = new CapturingLogger();
        var d = CustomDescriptor(_fx.Host, _fx.ImapPort);
        var settings = ImapPresets.Resolve(d);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = wrongPassword });

        var act = async () => await ImapClientFactory.ConnectAndAuthenticateAsync(
            d, settings, secret, logger, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ImapTransportException>()).Which;
        ex.Signature.Should().Be("imap:auth-failed");
        ex.Message.Should().Be("imap:auth-failed");
        ex.Message.Should().NotContain(wrongPassword);
        logger.Lines.Should().NotContain(l => l.Contains(wrongPassword, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ssrf_attempt_to_metadata_host_never_opens_socket()
    {
        var logger = new CapturingLogger();
        // SSL custom host so settings resolve; NO plaintext opt-in, so the host validator's
        // link-local/metadata block is active and must reject the dial before any socket opens.
        var d = SslCustomDescriptor("169.254.169.254", 993);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = "x" });

        await using var src = new ImapSourceProvider(d, secret, logger);
        var result = await src.TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().StartWith("imap:");
        // The validator must have blocked it BEFORE any connect log line was emitted.
        logger.Lines.Should().NotContain(l => l.Contains("Connecting to IMAP host 169.254.169.254", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plaintext_without_optin_is_rejected_before_socket()
    {
        var logger = new CapturingLogger();
        var d = CustomDescriptor(_fx.Host, _fx.ImapPort, allowPlaintext: false);
        var secret = new SecretBundle(new Dictionary<string, string> { ["password"] = "x" });

        // Resolve happens in the provider ctor → expect a configuration exception, no socket.
        var act = () => new ImapSourceProvider(d, secret, logger);
        act.Should().Throw<ImapConfigurationException>().Which.Message.Should().Contain("TLS");
        logger.Lines.Should().NotContain(l => l.Contains("Connecting to IMAP host", StringComparison.Ordinal));

        await Task.CompletedTask;
    }
}
