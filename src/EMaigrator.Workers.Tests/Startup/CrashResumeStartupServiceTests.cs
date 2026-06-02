using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Workers.Startup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Startup;

public sealed class CrashResumeStartupServiceTests
{
    [Fact]
    public async Task Reenqueues_each_not_done_running_migration_once()
    {
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var lookup = Substitute.For<IInterruptedJobLookup>();
        lookup.GetRunningMigrationsToResumeAsync(Arg.Any<CancellationToken>())
              .Returns(new List<Guid> { m1, m2 });
        var orch = Substitute.For<IJobOrchestrator>();

        var svc = new CrashResumeStartupService(lookup, orch, NullLogger<CrashResumeStartupService>.Instance);
        await svc.StartAsync(CancellationToken.None);

        await orch.Received(1).EnqueueMigrationAsync(m1, Arg.Any<CancellationToken>());
        await orch.Received(1).EnqueueMigrationAsync(m2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_running_migrations_enqueues_nothing()
    {
        var lookup = Substitute.For<IInterruptedJobLookup>();
        lookup.GetRunningMigrationsToResumeAsync(Arg.Any<CancellationToken>())
              .Returns(new List<Guid>());
        var orch = Substitute.For<IJobOrchestrator>();

        var svc = new CrashResumeStartupService(lookup, orch, NullLogger<CrashResumeStartupService>.Instance);
        await svc.StartAsync(CancellationToken.None);

        await orch.DidNotReceive().EnqueueMigrationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await svc.StopAsync(CancellationToken.None); // no-op completes
    }
}
