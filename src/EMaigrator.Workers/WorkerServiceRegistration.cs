using System;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Orchestration;
using EMaigrator.Workers.Sessions;
using EMaigrator.Workers.Startup;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Workers;

public static class WorkerServiceRegistration
{
    public static IServiceCollection AddEmaigratorWorkers(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.Configure<OrchestrationOptions>(config.GetSection("Orchestration"));
        var orchestration = config.GetSection("Orchestration").Get<OrchestrationOptions>() ?? new OrchestrationOptions();
        var useInMemory = config.GetValue("Workers:UseInMemoryTransport", false);

        services.AddSingleton<IMigrationControlGate, RedisMigrationControlGate>();
        services.AddSingleton<IProviderSessionFactory, ProviderSessionFactory>();
        services.AddSingleton<StreamingCopierFactory>();
        // IJobOrchestrator publishes from outside any consume scope → bind to the singleton IBus
        // (resolvable from the root provider; a scoped IPublishEndpoint would fail scope validation).
        services.AddSingleton<IJobOrchestrator>(sp => new MassTransitJobOrchestrator(sp.GetRequiredService<IBus>()));
        services.AddHostedService<CrashResumeStartupService>();

        // Real EF/IMAP-backed per-message data-seams + safe empty collection lookups.
        services.AddWorkerDataSeams();

        services.AddMassTransit(x =>
        {
            x.AddConsumer<StartMigrationConsumer>();
            x.AddConsumer<MigrateFolderConsumer>();
            x.AddConsumer<MigrateBatchConsumer>();
            x.AddConsumer<MigrateBatchFaultConsumer>();
            x.AddConsumer<JobControlConsumer>();
            x.AddConsumer<MigrationCompletionConsumer>();

            if (useInMemory)
            {
                x.UsingInMemory((ctx, cfg) =>
                {
                    cfg.PrefetchCount = orchestration.ConsumerPrefetch;
                    cfg.UseMessageRetry(r => r.Immediate(orchestration.DlqRetryCount));
                    cfg.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    var host = config.GetConnectionString("RabbitMq") ?? "amqp://guest:guest@localhost:5672";
                    cfg.Host(new Uri(host));
                    cfg.PrefetchCount = orchestration.ConsumerPrefetch;
                    // Immediate retries, then the message is faulted → DLQ → MigrateBatchFaultConsumer.
                    cfg.UseMessageRetry(r => r.Immediate(orchestration.DlqRetryCount));
                    cfg.ConfigureEndpoints(ctx);
                });
            }
        });

        return services;
    }
}
