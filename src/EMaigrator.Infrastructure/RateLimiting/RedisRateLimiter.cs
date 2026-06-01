using System.Globalization;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EMaigrator.Infrastructure.RateLimiting;

/// <summary>
/// Distributed per-(provider, account) token bucket in Redis. TryAcquireAsync runs an atomic Lua
/// script (refill + decrement); PenalizeAsync sets a per-key penalty so a 429/Retry-After pauses
/// only that account's bucket while all other accounts keep flowing.
/// </summary>
public sealed class RedisRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _mux;
    private readonly RateLimitOptions _options;

    public RedisRateLimiter(IConnectionMultiplexer mux, IOptions<RateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(mux);
        ArgumentNullException.ThrowIfNull(options);
        _mux = mux;
        _options = options.Value;
    }

    private static string BucketKey(RateLimitKey k) => $"rl:{k.Provider.Value}:{k.Account}";

    private static string PenaltyKey(RateLimitKey k) => $"rlp:{k.Provider.Value}:{k.Account}";

    private BucketSpec Resolve(RateLimitKey k)
    {
        if (_options.Buckets.TryGetValue($"{k.Provider.Value}:{k.Account}", out var exact))
        {
            return exact;
        }

        if (_options.Buckets.TryGetValue(k.Provider.Value, out var byProvider))
        {
            return byProvider;
        }

        if (_options.Buckets.TryGetValue("default", out var def))
        {
            return def;
        }

        return new BucketSpec { RefillPerSecond = 10, Burst = 20 };
    }

    public async Task<bool> TryAcquireAsync(RateLimitKey key, int tokens, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        var spec = Resolve(key);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = await db.ScriptEvaluateAsync(
            TokenBucketScripts.Acquire,
            new RedisKey[] { BucketKey(key), PenaltyKey(key) },
            new RedisValue[]
            {
                spec.RefillPerSecond.ToString(CultureInfo.InvariantCulture),
                spec.Burst.ToString(CultureInfo.InvariantCulture),
                now.ToString(CultureInfo.InvariantCulture),
                tokens.ToString(CultureInfo.InvariantCulture),
            }).ConfigureAwait(false);
        return (long)result == 1;
    }

    public async Task PenalizeAsync(RateLimitKey key, TimeSpan retryAfter, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        await db.StringSetAsync(PenaltyKey(key), "1", retryAfter).ConfigureAwait(false);
    }
}
