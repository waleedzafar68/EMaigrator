using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Microsoft Graph <see cref="ISourceProvider"/>. Uses application-permission access keyed by
/// the target mailbox's UPN (Users[accountEmail]); reads bodies as raw MIME via the $value
/// endpoint, never buffering them onto the canonical record (streaming pass-through; DESIGN.md §6/§10).
/// </summary>
public sealed class GraphSourceProvider : ISourceProvider
{
    private readonly GraphServiceClient _client;
    private readonly string _accountEmail;

    public GraphSourceProvider(GraphServiceClient client, string accountEmail)
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

    public async Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var nodes = await FetchFolderNodesAsync(ct).ConfigureAwait(false);
        var wellKnown = ResolveWellKnown(nodes);
        return GraphFolderMapper.BuildTree(nodes, wellKnown);
    }

    public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
        FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(options);

        var nodes = await FetchFolderNodesAsync(ct).ConfigureAwait(false);
        var wellKnown = ResolveWellKnown(nodes);
        var idsByPath = GraphFolderMapper.BuildIdIndex(nodes, wellKnown);
        var folderId = GraphFolderMapper.ResolveFolderId(folder, idsByPath)
            ?? throw new GraphConfigurationException($"Source folder '{folder}' was not found in the mailbox.");

        var filter = BuildDateFilter(options);

        var page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
            .GetAsync(
                rc =>
                {
                    rc.QueryParameters.Top = 50;
                    if (filter is not null)
                    {
                        rc.QueryParameters.Filter = filter;
                    }
                },
                ct)
            .ConfigureAwait(false);

        while (page is not null)
        {
            foreach (var message in page.Value ?? [])
            {
                ct.ThrowIfCancellationRequested();
                var messageId = message.Id!;
                yield return GraphMessageMapper.ToCanonical(
                    message,
                    token => _client.Users[_accountEmail].Messages[messageId].Content.GetAsync(cancellationToken: token)!);
            }

            if (string.IsNullOrEmpty(page.OdataNextLink))
            {
                break;
            }

            page = await _client.Users[_accountEmail].MailFolders[folderId].Messages
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct).ConfigureAwait(false);
        }
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

    private static string? BuildDateFilter(ReadOptions options)
    {
        var clauses = new List<string>();
        if (options.Since is { } since)
        {
            clauses.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"receivedDateTime ge {since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}"));
        }

        if (options.Before is { } before)
        {
            clauses.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"receivedDateTime lt {before.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}"));
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
