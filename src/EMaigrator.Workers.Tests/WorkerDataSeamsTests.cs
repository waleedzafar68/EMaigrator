using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers;
using EMaigrator.Workers.Persistence;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests;

public class WorkerDataSeamsTests
{
    [Fact]
    public void Registers_real_non_throwing_seams()
    {
        var services = new ServiceCollection();
        // The connection lookup + status writer need an IDbContextFactory; provide a substitute so the
        // graph builds (we only assert the resolved IMPLEMENTATION TYPE, not that it queries a DB).
        services.AddSingleton(Substitute.For<IDbContextFactory<EmaigratorDbContext>>());
        services.AddWorkerDataSeams();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IMigrationConnectionLookup>().Should().BeOfType<EfMigrationConnectionLookup>();
        sp.GetRequiredService<IMessageRefLister>().Should().BeOfType<ImapMessageRefLister>();
        sp.GetRequiredService<IMessageHydrator>().Should().BeOfType<ImapMessageHydrator>();
        sp.GetRequiredService<IMigrationStatusWriter>().Should().BeOfType<EfMigrationStatusWriter>();
    }
}
