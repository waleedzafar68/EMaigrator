using System;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Idempotency;
using FluentAssertions;
using Xunit;
using CoreFlags = EMaigrator.Core.Model.MessageFlags;
using Mk = MailKit;

namespace EMaigrator.Connectors.Imap.Tests;

public class ImapMessageMapperTests
{
    [Fact]
    public void MailKit_flags_map_to_core_flags()
    {
        var mk = Mk.MessageFlags.Seen | Mk.MessageFlags.Answered | Mk.MessageFlags.Flagged
                 | Mk.MessageFlags.Draft | Mk.MessageFlags.Deleted | Mk.MessageFlags.Recent;
        var core = ImapMessageMapper.ToCoreFlags(mk);
        core.Should().Be(CoreFlags.Seen | CoreFlags.Answered | CoreFlags.Flagged | CoreFlags.Draft | CoreFlags.Deleted);
    }

    [Fact]
    public void Core_flags_map_back_to_mailkit_flags()
    {
        var core = CoreFlags.Seen | CoreFlags.Flagged;
        var mk = ImapMessageMapper.ToMailKitFlags(core);
        mk.HasFlag(Mk.MessageFlags.Seen).Should().BeTrue();
        mk.HasFlag(Mk.MessageFlags.Flagged).Should().BeTrue();
        mk.HasFlag(Mk.MessageFlags.Answered).Should().BeFalse();
    }

    [Fact]
    public void Build_identity_input_uses_message_id_and_body_hash()
    {
        var input = ImapMessageMapper.BuildIdentityInput(
            messageId: "<abc@corp.example>",
            from: "a@corp.example",
            to: "b@corp.example",
            subject: "Hello",
            date: DateTimeOffset.Parse("2026-01-02T03:04:05Z", System.Globalization.CultureInfo.InvariantCulture),
            decodedBodySha256Hex: "deadbeef");

        input.MessageId.Should().Be("<abc@corp.example>");
        input.DecodedBodySha256Hex.Should().Be("deadbeef");
        input.From.Should().Be("a@corp.example");
        input.Subject.Should().Be("Hello");

        IdentityKey.Compute(input).Should().StartWith("mid:");
    }

    [Fact]
    public void Build_identity_input_null_message_id_falls_back_to_hash()
    {
        var input = ImapMessageMapper.BuildIdentityInput(
            messageId: null,
            from: "a@corp.example",
            to: "b@corp.example",
            subject: "Hello",
            date: DateTimeOffset.Parse("2026-01-02T03:04:05Z", System.Globalization.CultureInfo.InvariantCulture),
            decodedBodySha256Hex: "deadbeef");

        input.MessageId.Should().BeNull();
        IdentityKey.Compute(input).Should().StartWith("h:");
    }
}
