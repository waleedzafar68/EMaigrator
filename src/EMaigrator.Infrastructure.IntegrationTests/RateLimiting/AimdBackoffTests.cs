using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.RateLimiting;
using FluentAssertions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EMaigrator.Infrastructure.IntegrationTests.RateLimiting;

[Collection("redis")]
public class AimdBackoffTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer _mux = null!;

    public AimdBackoffTests(RedisFixture redis) => _redis = redis;

    public async Task InitializeAsync() => _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);

    public async Task DisposeAsync() => await _mux.DisposeAsync();

    private RedisRateLimiter NewLimiter() => new(_mux, Options.Create(new RateLimitOptions
    {
        Buckets = new() { ["default"] = new BucketSpec { RefillPerSecond = 100, Burst = 1 } },
    }));

    private static RateLimitKey Key() => new(new ProviderId("graph"), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Multiplier_decreases_on_penalty_and_recovers_on_success()
    {
        var limiter = NewLimiter();
        var key = Key();

        (await limiter.GetEffectiveMultiplierAsync(key)).Should().Be(1.0);

        await limiter.PenalizeAsync(key, TimeSpan.FromMilliseconds(1), default);
        await limiter.PenalizeAsync(key, TimeSpan.FromMilliseconds(1), default);
        var afterPenalties = await limiter.GetEffectiveMultiplierAsync(key);
        afterPenalties.Should().BeApproximately(0.25, 0.001, "two halvings: 1 -> 0.5 -> 0.25");

        await Task.Delay(5);
        for (var i = 0; i < 100; i++)
        {
            if (await limiter.TryAcquireAsync(key, 1, default)) { }
            await Task.Delay(2); // let bucket refill so grants continue
        }

        var recovered = await limiter.GetEffectiveMultiplierAsync(key);
        recovered.Should().BeGreaterThan(afterPenalties, "additive recovery on sustained success");
    }

    [Fact]
    public async Task Multiplier_is_floored()
    {
        var limiter = NewLimiter();
        var key = Key();
        for (var i = 0; i < 20; i++)
        {
            await limiter.PenalizeAsync(key, TimeSpan.FromMilliseconds(1), default);
        }

        (await limiter.GetEffectiveMultiplierAsync(key)).Should().BeGreaterThanOrEqualTo(0.05);
    }
}
