using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMaigrator.Workers.Copy;

/// <summary>Builds a StreamingMessageCopier bound to the destination provider resolved per batch.</summary>
public sealed class StreamingCopierFactory
{
    private readonly ILedger _ledger;
    private readonly IRateLimiter _limiter;

    public StreamingCopierFactory(ILedger ledger, IRateLimiter limiter)
    {
        _ledger = ledger;
        _limiter = limiter;
    }

    public StreamingMessageCopier For(IDestinationProvider dest)
        => new(_ledger, _limiter, dest, NullLogger<StreamingMessageCopier>.Instance);
}
