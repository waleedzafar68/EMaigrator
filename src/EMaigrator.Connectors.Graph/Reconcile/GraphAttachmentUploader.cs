using System.Diagnostics.CodeAnalysis;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.Messages.Item.Attachments.CreateUploadSession;

namespace EMaigrator.Connectors.Graph.Reconcile;

/// <summary>
/// Adds ONE attachment to an EXISTING Graph message. Small attachments (&lt;3 MB) go via a single
/// POST .../attachments; larger ones (3–150 MB) via an attachment upload session + chunked PUT
/// (LargeFileUploadTask). Inline parts preserve contentId. Bytes transit memory only.
/// </summary>
internal static class GraphAttachmentUploader
{
    internal const long UploadSessionThresholdBytes = 3 * 1024 * 1024;
    private const int MaxChunk = 4 * 1024 * 1024;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Transport/protocol failures normalize to a stable credential-free signature (CONTRACTS §8); caller records a normalized failure.")]
    public static async Task<bool> AddAsync(
        GraphServiceClient client, string accountEmail, string destMessageId,
        GraphAttachmentContent att, CancellationToken ct)
    {
        try
        {
            if (att.Size < UploadSessionThresholdBytes)
            {
                await using var s = att.OpenContent(ct);
                using var buffer = new MemoryStream();
                await s.CopyToAsync(buffer, ct).ConfigureAwait(false);
                var body = new FileAttachment
                {
                    Name = att.Name,
                    ContentType = att.ContentType,
                    IsInline = att.IsInline,
                    ContentId = att.ContentId,
                    ContentBytes = buffer.ToArray(),
                };
                await client.Users[accountEmail].Messages[destMessageId].Attachments
                    .PostAsync(body, cancellationToken: ct).ConfigureAwait(false);
                return true;
            }

            var session = await client.Users[accountEmail].Messages[destMessageId].Attachments
                .CreateUploadSession.PostAsync(new CreateUploadSessionPostRequestBody
                {
                    AttachmentItem = new AttachmentItem
                    {
                        AttachmentType = AttachmentType.File,
                        Name = att.Name,
                        ContentType = att.ContentType,
                        Size = att.Size,
                        IsInline = att.IsInline,
                        ContentId = att.ContentId,
                    },
                }, cancellationToken: ct).ConfigureAwait(false);

            await using var content = att.OpenContent(ct);
            var uploadTask = new LargeFileUploadTask<FileAttachment>(session, content, MaxChunk, client.RequestAdapter);
            var result = await uploadTask.UploadAsync(cancellationToken: ct).ConfigureAwait(false);
            return result.UploadSucceeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false; // GraphErrorNormalizer strips secrets where the caller surfaces a code.
        }
    }
}
