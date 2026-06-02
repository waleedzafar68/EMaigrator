using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using Microsoft.Graph.Models;

namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Pure mapping from a Graph <see cref="Message"/> to a <see cref="CanonicalMessage"/>
/// (CONTRACTS §1). The canonical record never holds the body — content is opened on demand
/// via the supplied factory (streaming pass-through; DESIGN.md §6/§10).
/// </summary>
public static class GraphMessageMapper
{
    public static CanonicalMessage ToCanonical(Message message, Func<CancellationToken, Task<Stream>> openContent)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(openContent);

        var internetMessageId = message.InternetMessageId;
        var internalDate = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UnixEpoch;

        // IdentityKey: when a Message-ID exists it produces "mid:<normalized>" without needing the body
        // (CONTRACTS §1). DecodedBodySha256Hex is required by MessageIdentityInput; for a present
        // Message-ID it is not used in the result, so we pass empty.
        var identity = IdentityKey.Compute(new MessageIdentityInput
        {
            MessageId = internetMessageId,
            From = null,
            To = null,
            Subject = message.Subject,
            Date = internalDate,
            DecodedBodySha256Hex = string.Empty
        });

        return new CanonicalMessage
        {
            IdentityKey = identity,
            MessageId = internetMessageId,
            InternalDate = internalDate,
            Flags = MapFlags(message),
            Labels = message.Categories?.ToArray() ?? [],
            SizeBytes = message.Body?.Content?.Length ?? 0,
            Attachments = MapAttachments(message),
            Subject = message.Subject,
            OpenContentAsync = openContent
        };
    }

    private static MessageFlags MapFlags(Message message)
    {
        var flags = MessageFlags.None;
        if (message.IsRead == true)
        {
            flags |= MessageFlags.Seen;
        }

        if (message.IsDraft == true)
        {
            flags |= MessageFlags.Draft;
        }

        if (message.Flag?.FlagStatus == FollowupFlagStatus.Flagged)
        {
            flags |= MessageFlags.Flagged;
        }

        return flags;
    }

    private static CanonicalAttachmentInfo[] MapAttachments(Message message)
    {
        if (message.Attachments is not { Count: > 0 })
        {
            return [];
        }

        return message.Attachments
            .Select(a => new CanonicalAttachmentInfo(
                a.Name ?? "attachment",
                a.ContentType ?? "application/octet-stream",
                a.Size ?? 0))
            .ToArray();
    }
}
