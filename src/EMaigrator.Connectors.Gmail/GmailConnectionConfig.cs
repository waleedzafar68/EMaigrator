using System;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Validated, in-memory Gmail connection config. The service-account JSON is held only
/// transiently for credential construction and is never exposed via a public property,
/// never logged, and never written to disk (DESIGN.md §10).
/// </summary>
public sealed class GmailConnectionConfig
{
    private readonly string _serviceAccountJson;

    private GmailConnectionConfig(string delegatedUser, string serviceAccountJson)
    {
        DelegatedUser = delegatedUser;
        _serviceAccountJson = serviceAccountJson;
    }

    /// <summary>The mailbox being impersonated via domain-wide delegation.</summary>
    public string DelegatedUser { get; }

    /// <summary>Internal accessor for the factory only; not a public-data surface.</summary>
    internal string ServiceAccountJson => _serviceAccountJson;

    public static GmailConnectionConfig FromDescriptor(ConnectionDescriptor descriptor, SecretBundle secrets)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!descriptor.Settings.TryGetValue("accountEmail", out var email) || string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Gmail connection requires a non-empty 'accountEmail' (the delegated mailbox).", nameof(descriptor));

        if (!secrets.Values.TryGetValue("serviceAccountJson", out var json) || string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Gmail connection requires a 'serviceAccountJson' secret.", nameof(secrets));

        return new GmailConnectionConfig(email.Trim(), json);
    }
}
