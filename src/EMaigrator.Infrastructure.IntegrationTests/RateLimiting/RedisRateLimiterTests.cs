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
public class RedisRateLimiterTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer _mux = null!;

    public RedisRateLimiterTests(RedisFixture redis) => _redis = redis;

    public async Task InitializeAsync() => _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);

    public async Task DisposeAsync() => await _mux.DisposeAsync();

    private RedisRateLimiter NewLimiter(double refillPerSecond, int burst)
    {
        var opts = Options.Create(new RateLimitOptions
        {
            Buckets = new() { ["default"] = new BucketSpec { RefillPerSecond = refillPerSecond, Burst = burst } },
        });
        return new RedisRateLimiter(_mux, opts);
    }

    private static RateLimitKey Key() => new(new ProviderId("graph"), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Grants_up_to_burst_then_throttles()
    {
        var limiter = NewLimiter(refillPerSecond: 0.001, burst: 3);
        var key = Key();

        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeFalse("burst exhausted, refill negligible");
    }

    [Fact]
    public async Task Refills_over_time()
    {
        var limiter = NewLimiter(refillPerSecond: 100, burst: 1);
        var key = Key();

        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue();
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeFalse();

        await Task.Delay(200); // 100 tok/s * 0.2s = 20 tokens (capped at burst)
        (await limiter.TryAcquireAsync(key, 1, default)).Should().BeTrue("bucket refilled");
    }

    [Fact]
    public async Task Penalize_blocks_only_that_key_until_retry_after()
    {
        var limiter = NewLimiter(refillPerSecond: 1000, burst: 100);
        var penalized = Key();
        var other = Key();

        await limiter.PenalizeAsync(penalized, TimeSpan.FromMilliseconds(400), default);

        (await limiter.TryAcquireAsync(penalized, 1, default)).Should().BeFalse("under penalty");
        (await limiter.TryAcquireAsync(other, 1, default)).Should().BeTrue("other account unaffected");

        await Task.Delay(500);
        (await limiter.TryAcquireAsync(penalized, 1, default)).Should().BeTrue("penalty expired");
    }
}
