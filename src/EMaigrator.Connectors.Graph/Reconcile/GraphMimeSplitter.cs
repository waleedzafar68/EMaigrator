using MimeKit;
using MimeKit.Cryptography;

namespace EMaigrator.Connectors.Graph.Reconcile;

/// <summary>
/// Splits a parsed MIME message for the hybrid large-message write and attachment backfill:
/// enumerates attachment-ish parts (for backfill, Task 5) and produces a reduced MIME with the
/// largest parts removed (for the over-ceiling write path, Task 3). Bytes transit memory only.
/// </summary>
internal static class GraphMimeSplitter
{
    internal sealed record Split(byte[] ReducedMimeBytes, IReadOnlyList<GraphAttachmentContent> Stripped, bool IsSigned);

    /// <summary>Enumerate all attachment-ish parts (attachments + inline non-text bodies).</summary>
    public static IReadOnlyList<(MimePart Part, GraphAttachmentContent Content)> Attachments(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var result = new List<(MimePart, GraphAttachmentContent)>();
        foreach (var bp in message.BodyParts)
        {
            if (bp is MimePart mp && (mp.IsAttachment || !mp.ContentType.IsMimeType("text", "*")))
            {
                var size = DecodedSize(mp);
                var name = mp.FileName ?? mp.ContentId ?? "attachment";
                var captured = mp;
                var content = new GraphAttachmentContent(
                    name,
                    mp.ContentType.MimeType,
                    IsInline: mp.IsAttachment == false && mp.ContentId != null,
                    ContentId: mp.ContentId,
                    Size: size,
                    OpenContent: ct =>
                    {
                        var s = new MemoryStream();
                        captured.Content?.DecodeTo(s, ct);
                        s.Position = 0;
                        return s;
                    });
                result.Add((mp, content));
            }
        }

        return result;
    }

    public static bool IsSigned(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Body is MultipartSigned
            || message.Body?.ContentType.IsMimeType("application", "pkcs7-mime") == true;
    }

    /// <summary>Strip the largest attachment parts until base64(reduced) ≤ limit; return them for re-upload.</summary>
    public static Split Reduce(MimeMessage message, long base64Limit)
    {
        ArgumentNullException.ThrowIfNull(message);

        var stripped = new List<GraphAttachmentContent>();
        var atts = Attachments(message).OrderByDescending(a => a.Content.Size).ToList();
        foreach (var (part, content) in atts)
        {
            if (Base64Len(message) <= base64Limit)
            {
                break;
            }

            // MimeKit entities carry no parent back-reference → walk the tree to detach the part.
            if (message.Body is not null && RemovePart(message.Body, part))
            {
                stripped.Add(content);
            }
        }

        using var ms = new MemoryStream();
        message.WriteTo(ms);
        return new Split(ms.ToArray(), stripped, IsSigned(message));
    }

    private static bool RemovePart(MimeEntity root, MimePart target)
    {
        if (root is Multipart multipart)
        {
            for (var i = 0; i < multipart.Count; i++)
            {
                if (ReferenceEquals(multipart[i], target))
                {
                    multipart.RemoveAt(i);
                    return true;
                }

                if (RemovePart(multipart[i], target))
                {
                    return true;
                }
            }
        }
        else if (root is MessagePart messagePart && messagePart.Message?.Body is { } nested)
        {
            return RemovePart(nested, target);
        }

        return false;
    }

    private static long DecodedSize(MimePart part)
    {
        if (part.Content is null)
        {
            return 0;
        }

        using var c = new MemoryStream();
        part.Content.DecodeTo(c);
        return c.Length;
    }

    private static long Base64Len(MimeMessage m)
    {
        using var ms = new MemoryStream();
        m.WriteTo(ms);
        return (ms.Length * 4 / 3) + 4; // base64 expansion estimate
    }
}
