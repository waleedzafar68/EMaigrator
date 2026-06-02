using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Gmail source provider (CONTRACTS §2). Reads labels as folders and messages as raw RFC822,
/// streaming bodies through memory only (DESIGN.md §10).
/// </summary>
public sealed class GmailSourceProvider : ISourceProvider
{
    private readonly GmailService _service;
    private readonly string _userId;

    public GmailSourceProvider(GmailService service, string userId)
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
            var labels = await ListMappableLabelsAsync(ct).ConfigureAwait(false);
            long messageCount = labels.Sum(l => (long)(l.MessagesTotal ?? 0));
            return new ConnectionTestResult(true, labels.Count, messageCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // RawDetail is a generic, credential/mailbox-free diagnostic; the precise reason
            // lives in the normalized signature, never the impersonated address.
            return new ConnectionTestResult(false, 0, 0, GmailErrorNormalizer.Normalize(ex), "Gmail connection failed.");
        }
    }

    public async Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var labels = await ListMappableLabelsAsync(ct).ConfigureAwait(false);
        return labels
            .Select(l => new CanonicalFolder(
                GmailLabelMapper.LabelNameToFolderPath(l.Name),
                l.MessagesTotal ?? 0,
                MessageFlags.None))
            .ToList();
    }

    public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
        FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(options);

        var labelName = GmailLabelMapper.FolderPathToLabelName(folder);
        var labelMap = await BuildLabelMapAsync(ct).ConfigureAwait(false);
        var labelId = ResolveLabelId(labelName, labelMap);

        string? pageToken = null;
        do
        {
            var listReq = _service.Users.Messages.List(_userId);
            if (labelId is not null)
            {
                listReq.LabelIds = new[] { labelId };
            }

            listReq.Q = BuildQuery(options);
            listReq.PageToken = pageToken;
            var page = await listReq.ExecuteAsync(ct).ConfigureAwait(false);
            pageToken = page.NextPageToken;

            if (page.Messages is null)
            {
                yield break;
            }

            foreach (var stub in page.Messages)
            {
                ct.ThrowIfCancellationRequested();
                var getReq = _service.Users.Messages.Get(_userId, stub.Id);
                getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
                var full = await getReq.ExecuteAsync(ct).ConfigureAwait(false);
                yield return ToCanonical(full, labelMap);
            }
        }
        while (!string.IsNullOrEmpty(pageToken));
    }

    private static string? BuildQuery(ReadOptions options)
    {
        var parts = new List<string>();
        if (options.Since is { } since)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"after:{since.ToUnixTimeSeconds()}"));
        }

        if (options.Before is { } before)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"before:{before.ToUnixTimeSeconds()}"));
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static CanonicalMessage ToCanonical(GmailMessage m, IReadOnlyDictionary<string, string> labelMap)
    {
        var rawBytes = GmailRawCodec.DecodeBase64Url(m.Raw);
        var rfc822 = Encoding.UTF8.GetString(rawBytes);
        var messageId = ExtractHeader(rfc822, "Message-ID");
        var subject = ExtractHeader(rfc822, "Subject");

        var labelIds = (IReadOnlyCollection<string>)(m.LabelIds ?? new List<string>());
        var flags = GmailFlagMapper.ToFlags(labelIds);
        var labels = GmailFlagMapper.ToCanonicalLabels(labelIds, labelMap);

        var internalDate = m.InternalDate is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.UnixEpoch;

        var bodySha = Convert.ToHexStringLower(SHA256.HashData(ExtractDecodedBody(rfc822)));

        var identityKey = IdentityKey.Compute(new MessageIdentityInput
        {
            MessageId = messageId,
            Subject = subject,
            Date = internalDate,
            DecodedBodySha256Hex = bodySha,
        });

        // Capture bytes for the closure; never written to disk.
        var captured = rawBytes;
        return new CanonicalMessage
        {
            IdentityKey = identityKey,
            MessageId = messageId,
            InternalDate = internalDate,
            Flags = flags,
            Labels = labels,
            SizeBytes = m.SizeEstimate ?? captured.LongLength,
            Subject = subject,
            OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(captured, writable: false)),
        };
    }

    private static byte[] ExtractDecodedBody(string rfc822)
    {
        var idx = rfc822.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = rfc822.IndexOf("\n\n", StringComparison.Ordinal);
        }

        var body = idx < 0 ? string.Empty : rfc822[idx..].TrimStart('\r', '\n');
        return Encoding.UTF8.GetBytes(body);
    }

    private static string? ExtractHeader(string rfc822, string name)
    {
        foreach (var line in rfc822.Split('\n'))
        {
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
            {
                return line[(name.Length + 1)..].Trim().TrimEnd('\r');
            }

            if (line.Length == 0 || line == "\r")
            {
                break; // end of headers
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<Label>> ListMappableLabelsAsync(CancellationToken ct)
    {
        var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct).ConfigureAwait(false);
        return (resp.Labels ?? new List<Label>())
            .Where(l => l.Name is not null && GmailLabelMapper.IsMappableLabel(l.Name))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildLabelMapAsync(CancellationToken ct)
    {
        var resp = await _service.Users.Labels.List(_userId).ExecuteAsync(ct).ConfigureAwait(false);
        return (resp.Labels ?? new List<Label>())
            .Where(l => l.Id is not null && l.Name is not null)
            .ToDictionary(l => l.Id!, l => l.Name!);
    }

    private static string? ResolveLabelId(string labelName, IReadOnlyDictionary<string, string> labelMap)
    {
        if (GmailLabelMapper.IsSystemLabel(labelName))
        {
            return labelName; // system label ids equal their names
        }

        foreach (var kv in labelMap)
        {
            if (string.Equals(kv.Value, labelName, StringComparison.Ordinal))
            {
                return kv.Key;
            }
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();
        return ValueTask.CompletedTask;
    }
}
