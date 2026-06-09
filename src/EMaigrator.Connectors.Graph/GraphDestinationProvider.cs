using System.Diagnostics.CodeAnalysis;
using System.Text;
using EMaigrator.Connectors.Graph.Reconcile;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.Messages.Item.Move;
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
                await ApplyReadStateAsync(created?.Id, message.Flags, ct).ConfigureAwait(false);
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
                await ApplyReadStateAsync(created?.Id, message.Flags, ct).ConfigureAwait(false);
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

            await ApplyReadStateAsync(destMessageId, message.Flags, ct).ConfigureAwait(false);
            return new WriteResult(Written: true, DestMessageId: destMessageId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var n = GraphErrorNormalizer.Normalize(ex);
            return new WriteResult(Written: false, ErrorCode: n.Signature);
        }
    }

    /// <summary>
    /// Imports base64 RFC822 (text/plain) and returns the message in the destination folder.
    /// Graph's FOLDER-scoped endpoint (.../mailFolders/{id}/messages) silently rejects a text/plain MIME
    /// body with 400 "UnableToDeserializePostBody" — there it only deserializes a JSON message resource,
    /// despite the docs listing it as a MIME target. The TOP-LEVEL endpoint (.../messages) DOES accept
    /// MIME: it creates the message as a draft in Drafts, which we then move into the destination folder
    /// (move re-keys the message id). Proven live end-to-end in GraphDestinationLiveTests.
    /// </summary>
    private async Task<Message?> ImportMimeAsync(string folderId, string base64Mime, CancellationToken ct)
    {
        // Start from the typed top-level builder so the URL template + base URL are correct, then override
        // the body with base64 RFC822 as text/plain (the typed PostAsync only sends JSON).
        var builder = _client.Users[_accountEmail].Messages;
        var requestInfo = builder.ToPostRequestInformation(new Message());
        requestInfo.Headers.Clear();
        requestInfo.SetStreamContent(
            new MemoryStream(Encoding.ASCII.GetBytes(base64Mime)), "text/plain");

        var errorMapping = new Dictionary<string, ParsableFactory<IParsable>>(StringComparer.Ordinal)
        {
            ["4XX"] = ODataError.CreateFromDiscriminatorValue,
            ["5XX"] = ODataError.CreateFromDiscriminatorValue,
        };

        var draft = await _client.RequestAdapter
            .SendAsync(requestInfo, Message.CreateFromDiscriminatorValue, errorMapping, ct)
            .ConfigureAwait(false);
        if (draft?.Id is not { } draftId)
        {
            return draft;
        }

        // Move the imported draft from Drafts into the real destination folder. The move returns the
        // message with its new (post-move) id, which is what callers persist and address attachments by.
        var moved = await _client.Users[_accountEmail].Messages[draftId].Move
            .PostAsync(new MovePostRequestBody { DestinationId = folderId }, cancellationToken: ct)
            .ConfigureAwait(false);
        return moved ?? draft;
    }

    /// <summary>
    /// Preserves the source read/unread state on the imported message. Graph marks every MIME-imported
    /// message <c>isDraft=true</c> and offers no supported way to clear that flag after creation, but the
    /// first-class <c>isRead</c> property IS mutable — so we at least carry read state across faithfully.
    /// </summary>
    private async Task ApplyReadStateAsync(string? messageId, MessageFlags flags, CancellationToken ct)
    {
        if (messageId is null)
        {
            return;
        }

        await _client.Users[_accountEmail].Messages[messageId]
            .PatchAsync(new Message { IsRead = flags.HasFlag(MessageFlags.Seen) }, cancellationToken: ct)
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
        FolderPath folder, DateTimeOffset? since, DateTimeOffset? before,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var folderId = await ResolveExistingFolderIdAsync(folder, ct).ConfigureAwait(false);
        if (folderId is null)
        {
            yield break; // missing/empty destination folder → nothing to reconcile against
        }

        // A date-scoped reconcile restricts the dest scan to the same received-date window, so a small
        // window reads only the relevant slice instead of the whole (possibly huge) folder.
        var filter = BuildReceivedFilter(since, before);
        var page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
            .GetAsync(
                rc =>
                {
                    rc.QueryParameters.Select = ["internetMessageId", "hasAttachments"];
                    rc.QueryParameters.Top = 100;
                    if (filter is not null)
                    {
                        rc.QueryParameters.Filter = filter;
                    }
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
        // Select only properties of the base microsoft.graph.attachment type that we actually read.
        // 'contentId' lives on fileAttachment, not the base type — selecting it 400s the whole scan.
        var page = await _client.Users[_accountEmail].Messages[messageId].Attachments
            .GetAsync(
                rc => rc.QueryParameters.Select = ["name", "size", "contentType"],
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
        try
        {
            var created = parentId is null
                ? await _client.Users[_accountEmail].MailFolders
                    .PostAsync(body, cancellationToken: ct).ConfigureAwait(false)
                : await _client.Users[_accountEmail].MailFolders[parentId].ChildFolders
                    .PostAsync(body, cancellationToken: ct).ConfigureAwait(false);
            return created!.Id!;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 409
            || string.Equals(ex.Error?.Code, "ErrorFolderExists", StringComparison.OrdinalIgnoreCase))
        {
            // Idempotent: the folder already exists (e.g. a prior partial run, or a concurrent create).
            // Re-resolve the existing child by name rather than faulting the whole job.
            return await FindChildByNameAsync(parentId, displayName, ct).ConfigureAwait(false)
                ?? throw new GraphConfigurationException(
                    $"Folder '{displayName}' reported as already existing but could not be re-resolved.");
        }
    }

    private async Task<string?> FindChildByNameAsync(string? parentId, string displayName, CancellationToken ct)
    {
        var page = parentId is null
            ? await _client.Users[_accountEmail].MailFolders
                .GetAsync(rc => rc.QueryParameters.Top = 100, ct).ConfigureAwait(false)
            : await _client.Users[_accountEmail].MailFolders[parentId].ChildFolders
                .GetAsync(rc => rc.QueryParameters.Top = 100, ct).ConfigureAwait(false);

        return page?.Value?
            .FirstOrDefault(f => string.Equals(f.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private async Task<string?> ResolveExistingFolderIdAsync(FolderPath folder, CancellationToken ct)
    {
        var nodes = await FetchFolderNodesAsync(ct).ConfigureAwait(false);
        var wellKnown = ResolveWellKnown(nodes);
        var idsByPath = GraphFolderMapper.BuildIdIndex(nodes, wellKnown);
        return GraphFolderMapper.ResolveFolderId(folder, idsByPath);
    }

    private async Task<IReadOnlyList<GraphMailFolderNode>> FetchFolderNodesAsync(CancellationToken ct)
    {
        // Collect the COMPLETE folder set first; root-vs-orphan classification needs every node's id,
        // because live Graph reports a top-level folder's parent as the root's real id (not "msgfolderroot").
        var raw = new List<MailFolder>();
        var page = await _client.Users[_accountEmail].MailFolders
            .GetAsync(rc => rc.QueryParameters.Top = 100, ct).ConfigureAwait(false);

        while (page is not null)
        {
            raw.AddRange(page.Value ?? []);

            if (string.IsNullOrEmpty(page.OdataNextLink))
            {
                break;
            }

            page = await _client.Users[_accountEmail].MailFolders
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        return GraphMailFolderNode.BuildFromGraph(raw);
    }

    // Graph OData $filter for the received-date window (UTC, second precision). Null when unscoped.
    private static string? BuildReceivedFilter(DateTimeOffset? since, DateTimeOffset? before)
    {
        var parts = new List<string>(2);
        if (since is { } s)
        {
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"receivedDateTime ge {s.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}"));
        }

        if (before is { } b)
        {
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"receivedDateTime lt {b.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}"));
        }

        return parts.Count == 0 ? null : string.Join(" and ", parts);
    }

    private static GraphFolderWellKnown ResolveWellKnown(IReadOnlyList<GraphMailFolderNode> nodes)
    {
        string? ByName(string name) => nodes.FirstOrDefault(n =>
            string.Equals(n.DisplayName, name, StringComparison.OrdinalIgnoreCase))?.Id;

        return new GraphFolderWellKnown(
            InboxId: ByName("Inbox"),
            DraftsId: ByName("Drafts"),
            SentItemsId: ByName("Sent Items"),
            DeletedItemsId: ByName("Deleted Items"),
            JunkEmailId: ByName("Junk Email"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
