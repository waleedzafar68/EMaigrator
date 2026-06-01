using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Opens one authenticated <see cref="ImapClient"/> for the resolved settings.
/// Validates the target host (anti-SSRF), enforces TLS, and authenticates with
/// either LOGIN (basic / app-password) or XOAUTH2. Secrets are pulled from the
/// transient <see cref="SecretBundle"/> and never logged.
/// </summary>
public static partial class ImapClientFactory
{
    public static SaslMechanismOAuth2 BuildOAuth2Mechanism(string accountEmail, string accessToken)
        => new(accountEmail, accessToken);

    public static string RequireSecret(IReadOnlyDictionary<string, string> values, string key)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            return v;
        throw new ImapConfigurationException($"Required secret '{key}' was not present in the secret bundle.");
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Any transport/protocol failure is deliberately normalized to a stable, " +
                        "credential-free errorSignature (CONTRACTS §8); the original exception (which may " +
                        "echo a credential in its message) is intentionally not propagated.")]
    public static async Task<ImapClient> ConnectAndAuthenticateAsync(
        ConnectionDescriptor descriptor,
        ImapConnectionSettings settings,
        SecretBundle secrets,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(logger);

        // Anti-SSRF: the only host we may dial is the one the preset/allowlist permits.
        // This runs BEFORE any socket is opened and BEFORE the connect log line is emitted.
        ImapHostValidator.Validate(descriptor, settings.Host);

        var client = new ImapClient();
        try
        {
            var secureOption = settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
            LogConnecting(logger, settings.Host, settings.Port, settings.UseSsl, settings.AccountEmail);
            await client.ConnectAsync(settings.Host, settings.Port, secureOption, ct).ConfigureAwait(false);

            if (descriptor.Auth == AuthMethod.ImapOAuthXoauth2)
            {
                var token = RequireSecret(secrets.Values, "accessToken");
                await client.AuthenticateAsync(BuildOAuth2Mechanism(settings.AccountEmail, token), ct)
                    .ConfigureAwait(false);
            }
            else // ImapBasic
            {
                var password = RequireSecret(secrets.Values, "password");
                await client.AuthenticateAsync(settings.AccountEmail, password, ct).ConfigureAwait(false);
            }

            return client;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            // Re-throw a sanitized exception: the signature only, never the credential-bearing original.
            throw new ImapTransportException(ImapErrorNormalizer.Normalize(ex));
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connecting to IMAP host {Host}:{Port} (ssl={UseSsl}) for {Account}")]
    private static partial void LogConnecting(ILogger logger, string host, int port, bool useSsl, string account);
}
