using System.Diagnostics.CodeAnalysis;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Contracts;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Messaging;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Infrastructure.IntegrationTests.Messaging;

[Collection("rabbitmq")]
public class MassTransitOrchestratorTests
{
    private readonly RabbitMqFixture _rabbit;

    public MassTransitOrchestratorTests(RabbitMqFixture rabbit) => _rabbit = rabbit;

    public sealed class Received
    {
        public TaskCompletionSource<Guid> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class StartConsumer(Received received) : IConsumer<StartMigration>
    {
        public Task Consume(ConsumeContext<StartMigration> context)
        {
            received.Tcs.TrySetResult(context.Message.MailboxMigrationId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Enqueue_publishes_to_a_consumer()
    {
        var received = new Received();
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.Configure<OrchestrationOptions>(_ => { });
        services.AddMassTransit(x =>
        {
            x.AddConsumer<StartConsumer>();
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(_rabbit.ConnectionString));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        await using var sp = services.BuildServiceProvider(true);
        var bus = sp.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            // IPublishEndpoint is registered scoped by MassTransit; resolve it (and the orchestrator's
            // deps) from a DI scope so scope-validation (BuildServiceProvider(true)) is satisfied.
            using var scope = sp.CreateScope();
            var orchestrator = new MassTransitJobOrchestrator(
                scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(),
                scope.ServiceProvider.GetRequiredService<IBus>());
            var id = Guid.NewGuid();
            await orchestrator.EnqueueMigrationAsync(id, default);

            var completed = await Task.WhenAny(received.Tcs.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            completed.Should().Be(received.Tcs.Task, "consumer should receive StartMigration");
            (await received.Tcs.Task).Should().Be(id);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    public sealed class DlqState
    {
        // Mutable field (not a property) so Interlocked.Increment can take it by ref.
        [SuppressMessage("Design", "CA1051:Do not declare visible instance fields",
            Justification = "Interlocked.Increment requires a field passed by ref; test-only counter.")]
        public int Attempts;
    }

    public sealed class PoisonConsumer(DlqState state) : IConsumer<StartMigration>
    {
        public Task Consume(ConsumeContext<StartMigration> context)
        {
            Interlocked.Increment(ref state.Attempts);
            throw new InvalidOperationException("poison");
        }
    }

    public sealed class FaultConsumer(TaskCompletionSource faulted) : IConsumer<Fault<StartMigration>>
    {
        public Task Consume(ConsumeContext<Fault<StartMigration>> context)
        {
            _ = context;
            faulted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Poison_message_faults_after_configured_retries()
    {
        var state = new DlqState();
        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(faulted);
        services.AddMassTransit(x =>
        {
            x.AddConsumer<PoisonConsumer>(c => c.UseMessageRetry(r => r.Immediate(3)));
            x.AddConsumer<FaultConsumer>();
            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(new Uri(_rabbit.ConnectionString));
                cfg.ConfigureEndpoints(ctx);
            });
        });
        await using var sp = services.BuildServiceProvider(true);
        var bus = sp.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            using var scope = sp.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPublishEndpoint>()
                .Publish(new StartMigration(Guid.NewGuid()));
            var completed = await Task.WhenAny(faulted.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            completed.Should().Be(faulted.Task, "after retries the message faults (DLQ path)");
            state.Attempts.Should().BeGreaterThanOrEqualTo(4, "1 initial + 3 retries before fault");
        }
        finally
        {
            await bus.StopAsync();
        }
    }
}
