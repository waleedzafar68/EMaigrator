using EMaigrator.Core.Model;
using MimeKit;

namespace EMaigrator.Connectors.Gmail;

/// <summary>Parses raw RFC822 (structure only) to enumerate attachment metadata — never persists bytes.</summary>
public static class GmailMimeAttachments
{
    public static IReadOnlyList<CanonicalAttachmentInfo> Read(byte[] rawMime)
    {
        ArgumentNullException.ThrowIfNull(rawMime);
        using var ms = new MemoryStream(rawMime, writable: false);
        var message = MimeMessage.Load(ms);

        var list = new List<CanonicalAttachmentInfo>();
        foreach (var part in message.BodyParts)
        {
            // Attachments AND inline non-text parts (e.g. embedded images) both count.
            if (part is MimePart mp && (mp.IsAttachment || !mp.ContentType.IsMimeType("text", "*")))
            {
                long size = 0;
                if (mp.Content is { } content)
                {
                    using var counter = new MemoryStream();
                    content.DecodeTo(counter);
                    size = counter.Length;
                }

                var name = mp.FileName ?? mp.ContentId ?? "attachment";
                list.Add(new CanonicalAttachmentInfo(name, mp.ContentType.MimeType, size));
            }
        }

        return list;
    }
}
