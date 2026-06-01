using System;
using EMaigrator.Core.Idempotency;
using CoreFlags = EMaigrator.Core.Model.MessageFlags;
using MkFlags = MailKit.MessageFlags;

namespace EMaigrator.Connectors.Imap;

/// <summary>
/// Maps IMAP message metadata to the canonical model: flag translation and
/// idempotency-input construction. Body bytes are never read here — the body
/// hash is supplied by the caller (streaming pass-through; DESIGN.md §6/§10).
/// </summary>
public static class ImapMessageMapper
{
    public static CoreFlags ToCoreFlags(MkFlags flags)
    {
        var result = CoreFlags.None;
        if (flags.HasFlag(MkFlags.Seen)) result |= CoreFlags.Seen;
        if (flags.HasFlag(MkFlags.Answered)) result |= CoreFlags.Answered;
        if (flags.HasFlag(MkFlags.Flagged)) result |= CoreFlags.Flagged;
        if (flags.HasFlag(MkFlags.Draft)) result |= CoreFlags.Draft;
        if (flags.HasFlag(MkFlags.Deleted)) result |= CoreFlags.Deleted;
        return result;
    }

    public static MkFlags ToMailKitFlags(CoreFlags flags)
    {
        var result = MkFlags.None;
        if (flags.HasFlag(CoreFlags.Seen)) result |= MkFlags.Seen;
        if (flags.HasFlag(CoreFlags.Answered)) result |= MkFlags.Answered;
        if (flags.HasFlag(CoreFlags.Flagged)) result |= MkFlags.Flagged;
        if (flags.HasFlag(CoreFlags.Draft)) result |= MkFlags.Draft;
        if (flags.HasFlag(CoreFlags.Deleted)) result |= MkFlags.Deleted;
        return result;
    }

    public static MessageIdentityInput BuildIdentityInput(
        string? messageId, string? from, string? to, string? subject,
        DateTimeOffset? date, string decodedBodySha256Hex)
        => new()
        {
            MessageId = string.IsNullOrWhiteSpace(messageId) ? null : messageId,
            From = from,
            To = to,
            Subject = subject,
            Date = date,
            DecodedBodySha256Hex = decodedBodySha256Hex,
        };
}
