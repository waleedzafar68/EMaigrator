using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Copy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Copy;

public sealed class StreamingMessageCopierTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly RateLimitKey DestKey = new(new ProviderId("graph"), "dest@biz.com");
    private static readonly FolderPath Src = FolderPath.Parse("Inbox");
    private static readonly FolderPath Dst = FolderPath.Parse("Inbox");

    private sealed class TrackedStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public TrackedStream(byte[] data) : base(data) { }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private static CanonicalMessage Msg(string key, TrackedStream stream) => new()
    {
        IdentityKey = key,
        InternalDate = DateTimeOffset.UtcNow,
        SizeBytes = stream.Length,
        OpenContentAsync = _ => Task.FromResult<Stream>(stream)
    };

    private static StreamingMessageCopier Sut(ILedger ledger, IRateLimiter limiter, IDestinationProvider dest)
        => new(ledger, limiter, dest, NullLogger<StreamingMessageCopier>.Instance);

    // Simulates a real destination provider: it opens the message's content stream on demand and
    // disposes it. (The copier no longer pre-opens the source body — that avoids a double fetch.)
    private static async Task<WriteResult> WriteOpeningAndDisposing(CanonicalMessage message)
    {
        await using var content = await message.OpenContentAsync(CancellationToken.None);
        return new WriteResult(true, "dest-1");
    }

    [Fact]
    public async Task Skips_when_ledger_says_done_without_writing()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:abc", Arg.Any<CancellationToken>()).Returns(true);
        var limiter = Substitute.For<IRateLimiter>();
        var dest = Substitute.For<IDestinationProvider>();
        var stream = new TrackedStream(new byte[] { 1, 2, 3 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:abc", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Skipped);
        await dest.DidNotReceive().WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>());
        await limiter.DidNotReceive().TryAcquireAsync(Arg.Any<RateLimitKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throttled_when_no_token()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:t", Arg.Any<CancellationToken>()).Returns(false);
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(DestKey, 1, Arg.Any<CancellationToken>()).Returns(false);
        var dest = Substitute.For<IDestinationProvider>();
        var stream = new TrackedStream(new byte[] { 1 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:t", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Throttled);
        await dest.DidNotReceive().WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>());
        await ledger.DidNotReceive().MarkAsync(Mid, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LedgerStatus>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Migrated_writes_marks_and_disposes_stream()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:ok", Arg.Any<CancellationToken>()).Returns(false);
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(DestKey, 1, Arg.Any<CancellationToken>()).Returns(true);
        var dest = Substitute.For<IDestinationProvider>();
        dest.WriteMessageAsync(Dst, Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
            .Returns(ci => WriteOpeningAndDisposing((CanonicalMessage)ci[1]));
        var stream = new TrackedStream(new byte[] { 9, 9 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:ok", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Migrated);
        await ledger.Received(1).MarkAsync(Mid, "mid:ok", "Inbox", "Inbox", LedgerStatus.Migrated, null, Arg.Any<CancellationToken>());
        // The destination opened and disposed the single content stream; the copier never opened it.
        stream.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_throttle_write_returns_throttled_without_marking()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:429", Arg.Any<CancellationToken>()).Returns(false);
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(DestKey, 1, Arg.Any<CancellationToken>()).Returns(true);
        var dest = Substitute.For<IDestinationProvider>();
        // A provider-side 429 surfaces as a non-written WriteResult with a throttle error code.
        dest.WriteMessageAsync(Dst, Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WriteResult(false, null, "graph:429:throttled"));
        var stream = new TrackedStream(new byte[] { 1 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:429", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Throttled);
        // A transient throttle must NOT be checkpointed as terminal — the ledger is never marked, so
        // the message is retried after the bucket penalty rather than lost as a permanent failure.
        await ledger.DidNotReceive().MarkAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<LedgerStatus>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_write_marks_failed_with_error_code()
    {
        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, "mid:f", Arg.Any<CancellationToken>()).Returns(false);
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(DestKey, 1, Arg.Any<CancellationToken>()).Returns(true);
        var dest = Substitute.For<IDestinationProvider>();
        dest.WriteMessageAsync(Dst, Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WriteResult(false, null, "ErrMessageTooLarge"));
        var stream = new TrackedStream(new byte[] { 1 });

        var outcome = await Sut(ledger, limiter, dest)
            .CopyAsync(Mid, DestKey, Src, Dst, Msg("mid:f", stream), CancellationToken.None);

        outcome.Should().Be(CopyOutcome.Failed);
        await ledger.Received(1).MarkAsync(Mid, "mid:f", "Inbox", "Inbox", LedgerStatus.Failed, "ErrMessageTooLarge", Arg.Any<CancellationToken>());
    }
}
