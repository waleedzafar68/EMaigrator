using System;

namespace EMaigrator.Workers.Consumers;

/// <summary>Thrown to fault a batch so MassTransit redelivers it after a provider throttle (429).</summary>
public sealed class ThrottledRequeueException : Exception
{
    public ThrottledRequeueException(string message) : base(message) { }
}
