using System;
using System.Collections.Generic;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EMaigrator.Workers.Tests;

public sealed class WorkerServiceRegistrationTests
{
    private static ServiceProvider Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestration:BatchSize"] = "100",
                ["Orchestration:DlqRetryCount"] = "5",
                ["Orchestration:ConsumerPrefetch"] = "16",
                ["Workers:UseInMemoryTransport"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // External seams supplied by Infrastructure/connectors at runtime — substituted here.
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<ISecretStore>());
        services.AddSingleton(Substitute.For<ILedger>());
        services.AddSingleton(Substitute.For<IRateLimiter>());
        // The per-message data-seams are now self-registered by AddEmaigratorWorkers
        // (AddWorkerDataSeams); the EF-backed ones only need an IDbContextFactory (normally from
        // AddInfrastructure), substituted here so the graph composes.
        services.AddSingleton(Substitute.For<IDbContextFactory<EmaigratorDbContext>>());

        services.AddEmaigratorWorkers(config);
        return services.BuildServiceProvider(true);
    }

    [Fact]
    public async Task Registers_core_worker_services()
    {
        await using var provider = Build();
        provider.GetService<IJobOrchestrator>().Should().BeOfType<MassTransitJobOrchestrator>();
        provider.GetService<IMigrationControlGate>().Should().BeOfType<RedisMigrationControlGate>();
        provider.GetService<IProviderSessionFactory>().Should().BeOfType<ProviderSessionFactory>();
        provider.GetService<EMaigrator.Workers.Copy.StreamingCopierFactory>().Should().NotBeNull();
    }

    [Fact]
    public async Task Binds_orchestration_options_from_config()
    {
        await using var provider = Build();
        var opts = provider.GetRequiredService<IOptions<OrchestrationOptions>>().Value;
        opts.DlqRetryCount.Should().Be(5);
        opts.BatchSize.Should().Be(100);
        opts.ConsumerPrefetch.Should().Be(16);
    }

    [Fact]
    public async Task Registers_crash_resume_hosted_service()
    {
        await using var provider = Build();
        var hosted = provider.GetServices<IHostedService>();
        hosted.Should().Contain(h => h is CrashResumeStartupService);
    }
}
