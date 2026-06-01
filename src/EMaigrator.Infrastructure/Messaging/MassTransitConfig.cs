using EMaigrator.Core.Configuration;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Messaging;

/// <summary>
/// MassTransit/RabbitMQ composition for EMaigrator. Configures prefetch/concurrency and a
/// redelivery + move-to-error (DLQ) policy from <see cref="OrchestrationOptions"/>. Consumer
/// registration (the Workers) is supplied by the caller via <paramref name="configureConsumers"/>.
/// </summary>
public static class MassTransitConfig
{
    public static IServiceCollection AddEmaigratorMessaging(
        this IServiceCollection services,
        string rabbitConnectionString,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rabbitConnectionString);

        services.AddMassTransit(x =>
        {
            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((ctx, cfg) =>
            {
                var orch = ctx.GetRequiredService<IOptions<OrchestrationOptions>>().Value;
                cfg.Host(new Uri(rabbitConnectionString));
                cfg.PrefetchCount = (ushort)orch.ConsumerPrefetch;
                cfg.ConcurrentMessageLimit = orch.ConsumerPrefetch;

                cfg.UseMessageRetry(r => r.Immediate(orch.DlqRetryCount));
                cfg.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(2)));

                cfg.ConfigureEndpoints(ctx);
            });
        });

        return services;
    }
}
