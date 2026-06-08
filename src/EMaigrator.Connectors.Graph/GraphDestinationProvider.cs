using System.Diagnostics.CodeAnalysis;
using System.Text;
using EMaigrator.Connectors.Graph.Reconcile;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using MimeKit;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Microsoft Graph <see cref="IDestinationProvider"/>. Imports messages by POSTing the source's
/// raw MIME ($value) as base64 RFC822 with Content-Type text/plain, which preserves the original
/// headers and sentDateTime. Folder creation is idempotent (only missing child segments are
/// created); throttling is surfaced as a normalized, credential-free transient error for the
/// worker's rate-limiter to handle (ARCHITECTURE.md §5; DESIGN.md §6/§10 — bodies transit memory only).
/// </summary>
public sealed class GraphDestinationProvider : IDestinationProvider, IReconcilableDestination
{
    // Single-POST MIME import ceiling (length of the base64 text). Larger messages take the hybrid
    // path: import a reduced MIME (oversized parts stripped) + add those parts back via the
    // attachment uploader (POST <3MB / upload session 3-150MB). S/MIME messages are never stripped.
    private const long MimeImportMaxBase64Bytes = 4 * 1024 * 1024;

    private readonly GraphServiceClient _client;
    private readonly string _accountEmail;

    public GraphDestinationProvider(GraphServiceClient client, string accountEmail)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountEmail);
        _client = client;
        _accountEmail = accountEmail;
    }

    public ProviderId Id => GraphProviderPlugin.GraphProviderId;

    public ProviderConstraints Constraints => GraphConstraints.MS365;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any transport/protocol failure is normalized to a stable credential-free errorSignature (CONTRACTS §8).")]
    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var nodes = await FetchFolderNodesAsync(ct).ConfigureAwait(false);
            var messageCount = nodes.Sum(n => n.TotalItemCount);
            return new ConnectionTestResult(Ok: true, FolderCount: nodes.Count, MessageCount: messageCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var n = GraphErrorNormalizer.Normalize(ex);
            return new ConnectionTestResult(Ok: false, FolderCount: 0, MessageCount: 0, ErrorCode: n.Signature);
        }
    }

    public async Task EnsureFolderAsync(FolderPath folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (folder.IsRoot)
        {
            return;
        }

        var nodes = await FetchFolderNodesAsync(ct).ConfigureAwait(false);
        var wellKnown = ResolveWellKnown(nodes);
        var idsByPath = new Dictionary<string, string>(
            GraphFolderMapper.BuildIdIndex(nodes, wellKnown), StringComparer.Ordinal);

        string? parentId = null;
        var accumulated = new List<string>();

        foreach (var segment in folder.Segments)
        {
            accumulated.Add(segment);
            var currentPath = new FolderPath(accumulated.ToArray()).ToString();

            // Idempotent: an already-existing segment is a no-op (no POST issued for it).
            if (idsByPath.TryGetValue(currentPath, out var existingId))
            {
                parentId = existingId;
                continue;
            }

            var created = await CreateChildFolderAsync(parentId, segment, ct).ConfigureAwait(false);
            idsByPath[currentPath] = created;
            parentId = created;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any transport/protocol failure is normalized to a stable credential-free errorSignature (CONTRACTS §8).")]
    public async Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var folderId = await ResolveExistingFolderIdAsync(folder, ct).ConfigureAwait(false)
                ?? throw new GraphConfigurationException(
                    $"Destination folder '{folder}' does not exist; call EnsureFolderAsync first.");

            byte[] rawMime;
            await using (var content = await message.OpenContentAsync(ct).ConfigureAwait(false))
            await using (var buffer = new MemoryStream())
            {
                await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
                rawMime = buffer.ToArray();
            }

            var base64Mime = Convert.ToBase64String(rawMime);

            // Fast path: the whole MIME fits the single-POST ceiling → byte-identical to before.
            if (base64Mime.Length <= MimeImportMaxBase64Bytes)
            {
                var created = await ImportMimeAsync(folderId, base64Mime, ct).ConfigureAwait(false);
                return new WriteResult(Written: true, DestMessageId: created?.Id);
            }

            // Over-ceiling: parse and either (S/MIME) attempt a whole import, or strip the largest
            // parts to fit and add them back via the uploader. Bytes never touch disk/DB.
            using var mimeStream = new MemoryStream(rawMime);
            var parsed = await MimeMessage.LoadAsync(mimeStream, ct).ConfigureAwait(false);

            if (GraphMimeSplitter.IsSigned(parsed))
            {
                // Stripping would break the signature/envelope → attempt the whole import; if Graph
                // rejects the oversized body the catch below returns a normalized WriteResult(false).
                var created = await ImportMimeAsync(folderId, base64Mime, ct).ConfigureAwait(false);
                return new WriteResult(Written: true, DestMessageId: created?.Id);
            }

            var split = GraphMimeSplitter.Reduce(parsed, MimeImportMaxBase64Bytes);
            var reducedBase64 = Convert.ToBase64String(split.ReducedMimeBytes);
            var reduced = await ImportMimeAsync(folderId, reducedBase64, ct).ConfigureAwait(false);
            if (reduced?.Id is not { } destMessageId)
            {
                return new WriteResult(Written: false, ErrorCode: "graph:reduced-import-no-id");
            }

            foreach (var att in split.Stripped)
            {
                await GraphAttachmentUploader.AddAsync(_client, _accountEmail, destMessageId, att, ct)
                    .ConfigureAwait(false);
            }

            return new WriteResult(Written: true, DestMessageId: destMessageId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var n = GraphErrorNormalizer.Normalize(ex);
            return new WriteResult(Written: false, ErrorCode: n.Signature);
        }
    }

    /// <summary>POSTs base64 RFC822 as text/plain to .../messages (MIME import) and returns the created message.</summary>
    private async Task<Message?> ImportMimeAsync(string folderId, string base64Mime, CancellationToken ct)
    {
        // Start from the typed builder so the URL template + base URL are correct, then override the
        // body with base64 RFC822 as text/plain (the typed PostAsync only sends JSON).
        var builder = _client.Users[_accountEmail].MailFolders[folderId].Messages;
        var requestInfo = builder.ToPostRequestInformation(new Message());
        requestInfo.Headers.Clear();
        requestInfo.SetStreamContent(
            new MemoryStream(Encoding.ASCII.GetBytes(base64Mime)), "text/plain");

        var errorMapping = new Dictionary<string, ParsableFactory<IParsable>>(StringComparer.Ordinal)
        {
            ["4XX"] = ODataError.CreateFromDiscriminatorValue,
            ["5XX"] = ODataError.CreateFromDiscriminatorValue,
        };

        return await _client.RequestAdapter
            .SendAsync(requestInfo, Message.CreateFromDiscriminatorValue, errorMapping, ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(messageId);

        var folderId = await ResolveExistingFolderIdAsync(folder, ct).ConfigureAwait(false);
        if (folderId is null)
        {
            return false;
        }

        var escaped = messageId.Replace("'", "''", StringComparison.Ordinal);
        var page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
            .GetAsync(
                rc =>
                {
                    rc.QueryParameters.Filter = $"internetMessageId eq '{escaped}'";
                    rc.QueryParameters.Top = 1;
                    rc.QueryParameters.Select = ["id"];
                },
                ct)
            .ConfigureAwait(false);

        return page?.Value is { Count: > 0 };
    }

    /// <summary>
    /// Bulk metadata scan of a destination folder for reconcile: pages the folder's messages
    /// ($select internetMessageId,hasAttachments) and yields one digest each. Attachment metadata is
    /// fetched only for messages that have attachments. Never reads bodies. (IReconcilableDestination)
    /// </summary>
    public async IAsyncEnumerable<DestMessageDigest> ScanFolderAsync(
        FolderPath folder, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var folderId = await ResolveExistingFolderIdAsync(folder, ct).ConfigureAwait(false);
        if (folderId is null)
        {
            yield break; // missing/empty destination folder → nothing to reconcile against
        }

        var page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
            .GetAsync(
                rc =>
                {
                    rc.QueryParameters.Select = ["internetMessageId", "hasAttachments"];
                    rc.QueryParameters.Top = 100;
                },
                ct)
            .ConfigureAwait(false);

        while (page is not null)
        {
            foreach (var m in page.Value ?? [])
            {
                if (string.IsNullOrEmpty(m.InternetMessageId))
                {
                    continue; // the malformed long tail is matched by identity fallback, not this index
                }

                var atts = m.HasAttachments == true
                    ? await FetchAttachmentMetaAsync(m.Id!, ct).ConfigureAwait(false)
                    : (IReadOnlyList<CanonicalAttachmentInfo>)[];
                yield return new DestMessageDigest(m.InternetMessageId!, m.Id!, atts);
            }

            if (string.IsNullOrEmpty(page.OdataNextLink))
            {
                break;
            }

            page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Backfills ONLY the given missing attachments onto an existing destination message: opens the
    /// source content, parses the MIME, extracts each matching part (Name+ContentType, case-insensitive,
    /// multiset) and uploads it via <see cref="GraphAttachmentUploader"/>. A per-part failure increments
    /// the failure count and continues (partial success). Bytes transit memory only. (IReconcilableDestination)
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any transport/protocol failure is normalized to a stable credential-free errorSignature (CONTRACTS §8).")]
    public async Task<BackfillResult> BackfillAttachmentsAsync(
        FolderPath folder, string destMessageId, CanonicalMessage source,
        IReadOnlyList<CanonicalAttachmentInfo> missing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(missing);
        if (missing.Count == 0)
        {
            return new BackfillResult(0, 0);
        }

        MimeMessage parsed;
        await using (var content = await source.OpenContentAsync(ct).ConfigureAwait(false))
        {
            parsed = await MimeMessage.LoadAsync(content, ct).ConfigureAwait(false);
        }

        var available = GraphMimeSplitter.Attachments(parsed).ToList();
        int added = 0, failed = 0;
        string? lastError = null;

        foreach (var want in missing)
        {
            var hit = available.FirstOrDefault(a =>
                string.Equals(a.Content.Name, want.FileName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Content.ContentType, want.ContentType, StringComparison.OrdinalIgnoreCase));

            if (hit.Content is null)
            {
                failed++;
                lastError = "attachment-not-found-in-source";
                continue;
            }

            available.Remove(hit); // consume one (multiset)

            var ok = await GraphAttachmentUploader
                .AddAsync(_client, _accountEmail, destMessageId, hit.Content, ct).ConfigureAwait(false);
            if (ok)
            {
                added++;
            }
            else
            {
                failed++;
                lastError = "graph:attachment-upload-failed";
            }
        }

        return new BackfillResult(added, failed, lastError);
    }

    private async Task<IReadOnlyList<CanonicalAttachmentInfo>> FetchAttachmentMetaAsync(string messageId, CancellationToken ct)
    {
        var page = await _client.Users[_accountEmail].Messages[messageId].Attachments
            .GetAsync(
                rc => rc.QueryParameters.Select = ["name", "size", "contentType", "contentId", "isInline"],
                ct)
            .ConfigureAwait(false);

        var list = new List<CanonicalAttachmentInfo>();
        foreach (var a in page?.Value ?? [])
        {
            list.Add(new CanonicalAttachmentInfo(
                a.Name ?? "attachment", a.ContentType ?? "application/octet-stream", a.Size ?? 0));
        }

        return list;
    }

    private async Task<string> CreateChildFolderAsync(string? parentId, string displayName, CancellationToken ct)
    {
        var body = new MailFolder { DisplayName = displayName };
        var created = parentId is null
            ? await _client.Users[_accountEmail].MailFolders
                .PostAsync(body, cancellationToken: ct).ConfigureAwait(false)
            : await _client.Users[_accountEmail].MailFolders[parentId].ChildFolders
                .PostAsync(body, cancellationToken: ct).ConfigureAwait(false);
        return created!.Id!;
    }

    private async Task<string?> ResolveExistingFolderIdAsync(FolderPath folder, CancellationToken ct)
    {
        var nodes = await FetchFolderNodesAsync(ct).ConfigureAwait(false);
        var wellKnown = ResolveWellKnown(nodes);
        var idsByPath = GraphFolderMapper.BuildIdIndex(nodes, wellKnown);
        return GraphFolderMapper.ResolveFolderId(folder, idsByPath);
    }

    private async Task<List<GraphMailFolderNode>> FetchFolderNodesAsync(CancellationToken ct)
    {
        var nodes = new List<GraphMailFolderNode>();
        var page = await _client.Users[_accountEmail].MailFolders
            .GetAsync(rc => rc.QueryParameters.Top = 100, ct).ConfigureAwait(false);

        while (page is not null)
        {
            foreach (var f in page.Value ?? [])
            {
                // The mailbox root parent id is "msgfolderroot"; null it out so top-level folders
                // are treated as canonical roots by GraphFolderMapper (rather than skipped as orphans).
                nodes.Add(new GraphMailFolderNode(
                    f.Id!,
                    f.DisplayName ?? "(unnamed)",
                    string.Equals(f.ParentFolderId, "msgfolderroot", StringComparison.Ordinal) ? null : f.ParentFolderId,
                    f.TotalItemCount ?? 0));
            }

            if (string.IsNullOrEmpty(page.OdataNextLink))
            {
                break;
            }

            page = await _client.Users[_accountEmail].MailFolders
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        return nodes;
    }

    private static GraphFolderWellKnown ResolveWellKnown(IReadOnlyList<GraphMailFolderNode> nodes)
    {
        string? ByName(string name) => nodes.FirstOrDefault(n =>
            string.Equals(n.DisplayName, name, StringComparison.OrdinalIgnoreCase))?.Id;

        return new GraphFolderWellKnown(
            InboxId: ByName("Inbox"),
            DraftsId: ByName("Drafts"),
            SentItemsId: ByName("Sent Items"),
            DeletedItemsId: ByName("Deleted Items"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
