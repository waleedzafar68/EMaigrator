using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Gmail destination provider (CONTRACTS §2). Creates labels for folders (idempotent
/// labels.create), imports raw RFC822 via messages.import with
/// <c>internalDateSource=dateHeader</c> so the original date is preserved, applies the
/// destination label plus any preserved canonical labels and read/star state, and supports
/// rfc822msgid-based dedup. Throttling (429) is surfaced as a normalized, credential-free
/// transient error for the worker's rate-limiter to back off on rather than thrown
/// (ARCHITECTURE.md §5; DESIGN.md §6/§10 — bodies transit memory only).
/// </summary>
public sealed class GmailDestinationProvider : IDestinationProvider
{
    private readonly GmailService _service;
    private readonly string _userId;

    public GmailDestinationProvider(GmailService service, string userId)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _userId = string.IsNullOrWhiteSpace(userId) ? "me" : userId;
    }

    public ProviderId Id => new("gmail");

    public ProviderConstraints Constraints => GmailConstraints.Default;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any transport/protocol failure is normalized to a stable credential-free errorSignature (CONTRACTS §8); the impersonated mailbox is never surfaced.")]
    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct).ConfigureAwait(false);
            var count = resp.Labels?.Count(l => l.Name is not null && GmailLabelMapper.IsMappableLabel(l.Name)) ?? 0;
            return new ConnectionTestResult(true, count, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, 0, 0, GmailErrorNormalizer.Normalize(ex), "Gmail connection failed.");
        }
    }

    public async Task EnsureFolderAsync(FolderPath folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var labelName = GmailLabelMapper.FolderPathToLabelName(folder);
        if (string.IsNullOrEmpty(labelName) || GmailLabelMapper.IsSystemLabel(labelName))
        {
            return; // root / system labels always exist
        }

        var existing = await GetLabelIdAsync(labelName, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return; // idempotent: already present, no second create
        }

        await _service.Users.Labels.Create(
            new Label
            {
                Name = labelName,
                MessageListVisibility = "show",
                LabelListVisibility = "labelShow",
            },
            _userId).ExecuteAsync(ct).ConfigureAwait(false);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Any transport/protocol failure is normalized to a stable credential-free errorSignature (CONTRACTS §8); a 429 is returned (not thrown) so the worker can back off.")]
    public async Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            byte[] raw;
            await using (var stream = await message.OpenContentAsync(ct).ConfigureAwait(false))
            await using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                raw = buffer.ToArray();
            }

            var labelIds = await ResolveDestinationLabelIdsAsync(folder, message, ct).ConfigureAwait(false);

            var gmsg = new GmailMessage
            {
                Raw = GmailRawCodec.EncodeBase64Url(raw),
                LabelIds = labelIds,
            };

            var import = _service.Users.Messages.Import(gmsg, _userId);
            import.InternalDateSource =
                UsersResource.MessagesResource.ImportRequest.InternalDateSourceEnum.DateHeader;
            import.NeverMarkSpam = true;       // a migration must not silently spam-file
            import.ProcessForCalendar = false;

            // The Google API client omits a query parameter whose value equals its declared
            // default, and "internalDateSource" defaults to "dateHeader" in this SDK — so setting
            // the typed property above strips it from the wire request. We append it explicitly so
            // the import is unambiguously date-header-sourced regardless of any future default
            // change (preserving the original sent date — DESIGN.md §6).
            import.ModifyRequest = AppendDateHeaderSource;

            var result = await import.ExecuteAsync(ct).ConfigureAwait(false);
            return new WriteResult(true, result.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WriteResult(false, null, GmailErrorNormalizer.Normalize(ex));
        }
    }

    public async Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(messageId);

        var id = messageId.Trim().Trim('<', '>');
        var req = _service.Users.Messages.List(_userId);
        req.Q = string.Create(CultureInfo.InvariantCulture, $"rfc822msgid:{id}");
        var resp = await req.ExecuteAsync(ct).ConfigureAwait(false);
        return resp.Messages is { Count: > 0 };
    }

    private async Task<IList<string>> ResolveDestinationLabelIdsAsync(
        FolderPath folder, CanonicalMessage message, CancellationToken ct)
    {
        var ids = new List<string>();

        var folderLabel = GmailLabelMapper.FolderPathToLabelName(folder);
        var folderId = GmailLabelMapper.IsSystemLabel(folderLabel)
            ? folderLabel
            : await GetLabelIdAsync(folderLabel, ct).ConfigureAwait(false);
        if (folderId is not null)
        {
            ids.Add(folderId);
        }

        // Preserve canonical (user) labels; system labels map to their own id.
        foreach (var name in message.Labels)
        {
            if (GmailLabelMapper.IsSystemLabel(name))
            {
                if (!ids.Contains(name))
                {
                    ids.Add(name);
                }

                continue;
            }

            var lid = await GetLabelIdAsync(name, ct).ConfigureAwait(false);
            if (lid is not null && !ids.Contains(lid))
            {
                ids.Add(lid);
            }
        }

        // Read-state: unseen => keep UNREAD; Gmail models read as the absence of UNREAD.
        if (!message.Flags.HasFlag(MessageFlags.Seen) && !ids.Contains("UNREAD"))
        {
            ids.Add("UNREAD");
        }

        if (message.Flags.HasFlag(MessageFlags.Flagged) && !ids.Contains("STARRED"))
        {
            ids.Add("STARRED");
        }

        return ids;
    }

    private static void AppendDateHeaderSource(HttpRequestMessage request)
    {
        const string param = "internalDateSource=dateHeader";
        var builder = new UriBuilder(request.RequestUri!);
        var existing = builder.Query.TrimStart('?');
        builder.Query = existing.Length == 0 ? param : $"{existing}&{param}";
        request.RequestUri = builder.Uri;
    }

    private async Task<string?> GetLabelIdAsync(string labelName, CancellationToken ct)
    {
        var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct).ConfigureAwait(false);
        return resp.Labels?
            .FirstOrDefault(l => string.Equals(l.Name, labelName, StringComparison.Ordinal))?.Id;
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();
        return ValueTask.CompletedTask;
    }
}
