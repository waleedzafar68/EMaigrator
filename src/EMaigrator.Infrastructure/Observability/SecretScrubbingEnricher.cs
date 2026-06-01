using System;
using System.Linq;
using Serilog.Core;
using Serilog.Events;

namespace EMaigrator.Infrastructure.Observability;

/// <summary>
/// Redacts log properties whose names indicate secrets. Defense-in-depth: secrets should never be
/// logged in the first place, but this guarantees zero plaintext credentials reach any sink.
/// </summary>
public sealed class SecretScrubbingEnricher : ILogEventEnricher
{
    private const string Redacted = "***REDACTED***";

    private static readonly string[] SecretMarkers =
    {
        "password", "secret", "token", "apikey", "api_key", "clientsecret",
        "client_secret", "cipherblob", "credential", "authorization", "sajson", "privatekey",
    };

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        foreach (var prop in logEvent.Properties.ToArray())
        {
            if (IsSecretName(prop.Key))
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(prop.Key, Redacted));
            }
        }
    }

    private static bool IsSecretName(string name)
    {
        foreach (var marker in SecretMarkers)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
