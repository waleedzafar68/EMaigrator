using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Core.Tests.Contracts;

public class MessageContractsTests
{
    [Fact]
    public void StartMigration_CarriesId()
    {
        var id = Guid.NewGuid();
        new StartMigration(id).MailboxMigrationId.Should().Be(id);
    }

    [Fact]
    public void MigrateBatch_CarriesRefs()
    {
        var m = new MigrateBatch(Guid.NewGuid(), Guid.NewGuid(), "Inbox", "Inbox", new[] { "r1", "r2" });
        m.SourceMessageRefs.Should().Equal("r1", "r2");
        m.SourceFolder.Should().Be("Inbox");
    }

    [Fact]
    public void MigrationProgressEvent_HoldsCountsAndStatus()
    {
        var e = new MigrationProgressEvent(Guid.NewGuid(), 5, 10, "Inbox", 120.0, "Running");
        e.Migrated.Should().Be(5);
        e.Total.Should().Be(10);
        e.Status.Should().Be("Running");
    }

    [Fact]
    public void MigrationProgressEvent_carries_optional_reconcile_progress()
    {
        var e = new MigrationProgressEvent(Guid.NewGuid(), 0, 0, "/Inbox", 0, "Running",
            new ReconcileProgress(FoldersDone: 3, FolderTotal: 650, Copied: 40, Backfilled: 2, Skipped: 1200));
        e.Reconcile.Should().NotBeNull();
        e.Reconcile!.FolderTotal.Should().Be(650);
        e.Reconcile.Backfilled.Should().Be(2);

        // migrate path still compiles with no reconcile arg
        new MigrationProgressEvent(Guid.NewGuid(), 1, 2, null, 0, "Running").Reconcile.Should().BeNull();
    }

    [Fact]
    public void NeedsDecisionEvent_CarriesRemediationOptions()
    {
        var e = new NeedsDecisionEvent(Guid.NewGuid(), "OversizedMessage", "12 MB > 10 MB cap",
            new[] { RemediationAction.SkipMessage });
        e.Options.Should().ContainSingle().Which.Should().Be(RemediationAction.SkipMessage);
    }

    [Fact]
    public void Records_AreValueEqual()
    {
        var id = Guid.NewGuid();
        new StartMigration(id).Should().Be(new StartMigration(id));
    }
}
