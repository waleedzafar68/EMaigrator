using EMaigrator.Core.Abstractions;
using FluentAssertions;

namespace EMaigrator.Connectors.Graph.Tests.Reconcile;

/// <summary>
/// The reconcile capability is OPTIONAL and Graph/Exchange-only. Asserted at the type level so no
/// real provider client/session has to be constructed.
/// </summary>
public class ReconcilableCapabilityTests
{
    [Fact]
    public void Graph_destination_implements_IReconcilableDestination()
    {
        typeof(IReconcilableDestination).IsAssignableFrom(typeof(GraphDestinationProvider)).Should().BeTrue();
    }

    [Fact]
    public void Imap_destination_does_not_implement_IReconcilableDestination()
    {
        typeof(IReconcilableDestination)
            .IsAssignableFrom(typeof(EMaigrator.Connectors.Imap.ImapDestinationProvider)).Should().BeFalse();
    }

    [Fact]
    public void Gmail_destination_does_not_implement_IReconcilableDestination()
    {
        typeof(IReconcilableDestination)
            .IsAssignableFrom(typeof(EMaigrator.Connectors.Gmail.GmailDestinationProvider)).Should().BeFalse();
    }
}
