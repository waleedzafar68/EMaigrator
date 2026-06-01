using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// IMAP <see cref="ISourceProvider"/> on MailKit. Reads folders and streams raw
/// RFC822 message bytes; bodies transit memory only (DESIGN.md §10).
/// </summary>
public sealed class ImapSourceProvider : ISourceProvider
{
    private readonly ConnectionDescriptor _descriptor;
    private readonly ImapConnectionSettings _settings;
    private readonly SecretBundle _secrets;
    private readonly ILogger _logger;
    private ImapClient? _client;
    private char _separator = '/';

    public ImapSourceProvider(ConnectionDescriptor descriptor, SecretBundle secrets, ILogger? logger = null)
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

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var client = await EnsureClientAsync(ct).ConfigureAwait(false);
            // Inbox is non-null once the client is connected+authenticated (MailKit contract).
            var inbox = client.Inbox!;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
            var folders = await GetAllFoldersAsync(client, ct).ConfigureAwait(false);
            var messageCount = inbox.Count;
            return new ConnectionTestResult(true, folders.Count, messageCount);
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

    public async Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        var folders = await GetAllFoldersAsync(client, ct).ConfigureAwait(false);
        var result = new List<CanonicalFolder>(folders.Count);
        foreach (var f in folders)
        {
            await f.StatusAsync(StatusItems.Count, ct).ConfigureAwait(false);
            result.Add(new CanonicalFolder(
                ImapFolderMapper.ToFolderPath(f.FullName, _separator),
                f.Count));
        }
        return result;
    }

    public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
        FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(options);
        var client = await EnsureClientAsync(ct).ConfigureAwait(false);
        var imapFolder = await client.GetFolderAsync(ImapFolderMapper.ToServerName(folder, _separator), ct)
            .ConfigureAwait(false);
        await imapFolder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        var query = BuildQuery(options);
        var uids = query is null
            ? await imapFolder.SearchAsync(SearchQuery.All, ct).ConfigureAwait(false)
            : await imapFolder.SearchAsync(query, ct).ConfigureAwait(false);

        foreach (var uid in uids)
        {
            ct.ThrowIfCancellationRequested();
            var summaries = await imapFolder.FetchAsync(
                new[] { uid },
                MessageSummaryItems.UniqueId | MessageSummaryItems.Flags |
                MessageSummaryItems.InternalDate | MessageSummaryItems.Envelope |
                MessageSummaryItems.Size, ct).ConfigureAwait(false);
            var summary = summaries.FirstOrDefault();
            if (summary is null) continue;

            yield return await BuildMessageAsync(imapFolder, summary, ct).ConfigureAwait(false);
        }
    }

    private static async Task<CanonicalMessage> BuildMessageAsync(
        IMailFolder imapFolder, IMessageSummary summary, CancellationToken ct)
    {
        var env = summary.Envelope;
        var messageId = env?.MessageId;
        var from = env?.From?.ToString();
        var to = env?.To?.ToString();
        var subject = env?.Subject;
        var date = summary.InternalDate ?? env?.Date;

        // Compute the decoded-body hash for the identity key without holding the body:
        // fetch once into memory, hash decoded text, and reuse the bytes for streaming.
        var raw = await FetchRawAsync(imapFolder, summary.UniqueId, ct).ConfigureAwait(false);
        var bodyHash = ComputeDecodedBodySha256Hex(raw);

        var identityInput = ImapMessageMapper.BuildIdentityInput(
            messageId, from, to, subject, date, bodyHash);
        var identityKey = IdentityKey.Compute(identityInput);

        var uid = summary.UniqueId;
        return new CanonicalMessage
        {
            IdentityKey = identityKey,
            MessageId = messageId,
            InternalDate = (date ?? DateTimeOffset.UnixEpoch),
            Flags = ImapMessageMapper.ToCoreFlags(summary.Flags ?? MailKit.MessageFlags.None),
            SizeBytes = (long)(summary.Size ?? (uint)raw.Length),
            Subject = subject,
            OpenContentAsync = async token =>
            {
                var bytes = await FetchRawAsync(imapFolder, uid, token).ConfigureAwait(false);
                return new MemoryStream(bytes, writable: false);
            },
        };
    }

    private static async Task<byte[]> FetchRawAsync(IMailFolder folder, UniqueId uid, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var mime = await folder.GetMessageAsync(uid, ct).ConfigureAwait(false);
        await mime.WriteToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static string ComputeDecodedBodySha256Hex(byte[] rawRfc822)
    {
        using var src = new MemoryStream(rawRfc822, writable: false);
        var message = MimeKit.MimeMessage.Load(src);
        var bodyText = message.TextBody ?? message.HtmlBody ?? string.Empty;
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(bodyText));
        return Convert.ToHexStringLower(bytes);
    }

    private static SearchQuery? BuildQuery(ReadOptions options)
    {
        SearchQuery? q = null;
        if (options.Since is { } since)
            q = SearchQuery.DeliveredAfter(since.UtcDateTime);
        if (options.Before is { } before)
            q = q is null ? SearchQuery.DeliveredBefore(before.UtcDateTime) : q.And(SearchQuery.DeliveredBefore(before.UtcDateTime));
        return q;
    }

    private static async Task<List<IMailFolder>> GetAllFoldersAsync(ImapClient client, CancellationToken ct)
    {
        var ns = client.PersonalNamespaces.Count > 0 ? client.PersonalNamespaces[0] : null;
        var personal = ns is null
            ? (IList<IMailFolder>)Array.Empty<IMailFolder>()
            : await client.GetFoldersAsync(ns, false, ct).ConfigureAwait(false);
        // Inbox is non-null once the client is connected+authenticated (MailKit contract).
        var inbox = client.Inbox!;
        var all = new List<IMailFolder> { inbox };
        all.AddRange(personal.Where(f => !f.FullName.Equals(inbox.FullName, StringComparison.OrdinalIgnoreCase)));
        return all;
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
