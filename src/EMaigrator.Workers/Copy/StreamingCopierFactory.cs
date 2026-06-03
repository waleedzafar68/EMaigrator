using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Copy;

/// <summary>
/// Builds a <see cref="StreamingMessageCopier"/> for the destination provider resolved per batch.
/// Holds only the singleton <see cref="ILoggerFactory"/>; the per-consume-scope <see cref="ILedger"/>
/// and <see cref="IRateLimiter"/> are passed to <see cref="For"/> by the consumer, so this factory
/// stays a singleton without capturing any scoped service (no captive dependency on the scoped ledger).
/// </summary>
public sealed class StreamingCopierFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public StreamingCopierFactory(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public StreamingMessageCopier For(ILedger ledger, IRateLimiter limiter, IDestinationProvider dest)
        => new(ledger, limiter, dest, _loggerFactory.CreateLogger<StreamingMessageCopier>());
}
