namespace EMaigrator.Infrastructure.RateLimiting;

internal static class TokenBucketScripts
{
    // KEYS[1] = bucket hash key, KEYS[2] = penalty key
    // ARGV: 1 refillPerSecond, 2 burst, 3 nowMs, 4 requestedTokens, 5 additiveIncrease
    // Reads/writes a per-key 'mult' AIMD multiplier: effective refill = refillPerSecond * mult,
    // and each grant additively increases mult toward 1.0. Returns 1 if granted, 0 if throttled.
    public const string Acquire = @"
if redis.call('EXISTS', KEYS[2]) == 1 then
  return 0
end
local refill = tonumber(ARGV[1])
local burst = tonumber(ARGV[2])
local now = tonumber(ARGV[3])
local requested = tonumber(ARGV[4])
local inc = tonumber(ARGV[5])
local data = redis.call('HMGET', KEYS[1], 'tokens', 'ts', 'mult')
local tokens = tonumber(data[1])
local ts = tonumber(data[2])
local mult = tonumber(data[3])
if mult == nil then mult = 1.0 end
if tokens == nil then
  tokens = burst
  ts = now
end
local elapsed = (now - ts) / 1000.0
if elapsed < 0 then elapsed = 0 end
tokens = math.min(burst, tokens + elapsed * refill * mult)
local granted = 0
if tokens >= requested then
  tokens = tokens - requested
  granted = 1
  mult = math.min(1.0, mult + inc)
end
redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now, 'mult', mult)
redis.call('PEXPIRE', KEYS[1], 3600000)
return granted
";

    // KEYS[1] = bucket hash key. ARGV: 1 decreaseFactor, 2 floor.
    // Multiplicatively decreases the per-key 'mult' multiplier, floored at a minimum.
    public const string Penalize = @"
local data = redis.call('HMGET', KEYS[1], 'mult')
local mult = tonumber(data[1])
if mult == nil then mult = 1.0 end
mult = math.max(tonumber(ARGV[2]), mult * tonumber(ARGV[1]))
redis.call('HSET', KEYS[1], 'mult', mult)
redis.call('PEXPIRE', KEYS[1], 3600000)
return tostring(mult)
";
}
