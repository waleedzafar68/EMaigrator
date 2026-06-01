using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using CoreMessage = EMaigrator.Core.Model.CanonicalMessage;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// IMAP <see cref="IDestinationProvider"/> on MailKit. Creates folder hierarchy
/// honoring the server separator and APPENDs messages preserving InternalDate and
/// flags. Idempotency is the engine's job (ledger); ExistsByMessageId supports
/// non-empty-destination dedup.
/// </summary>
public sealed class ImapDestinationProvider : IDestinationProvider
{
    private readonly ConnectionDescriptor _descriptor;
    private readonly ImapConnectionSettings _settings;
    private readonly SecretBundle _secrets;
    private readonly ILogger _logger;
    private ImapClient? _client;
    private char _separator = '/';

    public ImapDestinationProvider(ConnectionDescriptor descriptor, SecretBundle secrets, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(secrets);
        _descriptor = descriptor;
        _settings = ImapPresets.Resolve(descriptor);
        _secrets = secrets;
        _logger = logger ?? NullLogger.Instance;
    }

    public ProviderId Id => new("imap");
    public ProviderConstraints Constraints => ImapConstraints.Default(_separator);

    private async Task<ImapClient> EnsureClientAsync(CancellationToken ct)
    {
        if (_client is { IsConnected: true, IsAuthenticated: true })
            return _client;
        _client = await ImapClientFactory.ConnectAndAuthenticateAsync(_descriptor, _settings, _secrets, _logger, ct)
            .ConfigureAwait(false);
        _separator = _client.PersonalNamespaces.Count > 0 ? _client.PersonalNamespaces[0].DirectorySeparator : '/';
        return _client;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any transport/protocol failure is normalized to a stable credential-free errorSignature (CONTRACTS §8).")]
    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var client = await EnsureClientAsync(ct).ConfigureAwait(false);
            // Inbox is non-null once the client is connected+authenticated (MailKit contract).
            var inbox = client.Inbox!;
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct).ConfigureAwait(false);
            // Prove append capability: APPEND a probe, then expunge it (non-destructive to user mail).
            var probe = new MimeMessage();
            probe.From.Add(new MailboxAddress("EMaigrator", _settings.AccountEmail));
            probe.To.Add(new MailboxAddress("EMaigrator", _settings.AccountEmail));
            probe.Subject = "EMaigrator connection probe";
            probe.MessageId = $"emaigrator-probe-{Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)}@emaigrator.local";
            probe.Body = new TextPart("plain") { Text = "probe" };
            var appended = await inbox.AppendAsync(probe, MailKit.MessageFlags.Deleted, DateTimeOffset.UtcNow, ct)
                .ConfigureAwait(false);
            if (appended is { } uid)
            {
                await inbox.AddFlagsAsync(new[] { uid }, MailKit.MessageFlags.Deleted, true, ct).ConfigureAwait(false);
                await inbox.ExpungeAsync(new[] { uid }, ct).ConfigureAwait(false);
            }
            return new ConnectionTestResult(true, 1, inbox.Count);
        }
        catch (ImapTransportException ex)
        {
            return new ConnectionTestResult(false, 0, 0, ex.Signature);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, 0, 0, ImapErrorNormalizer.Normalize(ex));
        }
    }

    public async Task EnsureFolderAsync(FolderPath folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.IsRoot) return;
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        // GetFolder(namespace) returns the namespace root folder; non-null for a valid namespace.
        var current = client.GetFolder(client.PersonalNamespaces.Count > 0
            ? client.PersonalNamespaces[0]
            : new FolderNamespace(_separator, string.Empty))!;

        foreach (var segment in folder.Segments)
        {
            IMailFolder child;
            try
            {
                // GetSubfolder/Create return the resolved folder; non-null or they throw.
                child = (await current.GetSubfolderAsync(segment, ct).ConfigureAwait(false))!;
            }
            catch (FolderNotFoundException)
            {
                child = (await current.CreateAsync(segment, isMessageFolder: true, ct).ConfigureAwait(false))!;
            }
            current = child;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any transport/protocol failure is normalized to a stable credential-free errorSignature (CONTRACTS §8).")]
    public async Task<WriteResult> WriteMessageAsync(FolderPath folder, CoreMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var client = await EnsureClientAsync(ct).ConfigureAwait(false);
            await EnsureFolderAsync(folder, ct).ConfigureAwait(false);
            var imapFolder = await client.GetFolderAsync(ImapFolderMapper.ToServerName(folder, _separator), ct)
                .ConfigureAwait(false);

            await using var content = await message.OpenContentAsync(ct).ConfigureAwait(false);
            var mime = await MimeMessage.LoadAsync(content, ct).ConfigureAwait(false);
            var flags = ImapMessageMapper.ToMailKitFlags(message.Flags);
            var appended = await imapFolder.AppendAsync(mime, flags, message.InternalDate, ct).ConfigureAwait(false);
            return new WriteResult(true, appended?.ToString());
        }
        catch (ImapTransportException ex)
        {
            return new WriteResult(false, null, ex.Signature);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WriteResult(false, null, ImapErrorNormalizer.Normalize(ex));
        }
    }

    public async Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(messageId);
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        var imapFolder = await client.GetFolderAsync(ImapFolderMapper.ToServerName(folder, _separator), ct)
            .ConfigureAwait(false);
        await imapFolder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
        var trimmed = messageId.Trim('<', '>');
        var uids = await imapFolder.SearchAsync(SearchQuery.HeaderContains("Message-Id", trimmed), ct)
            .ConfigureAwait(false);
        return uids.Count > 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(true).ConfigureAwait(false);
            _client.Dispose();
            _client = null;
        }
        GC.SuppressFinalize(this);
    }
}
