namespace EMaigrator.Infrastructure.RateLimiting;

internal static class TokenBucketScripts
{
    // KEYS[1] = bucket hash key, KEYS[2] = penalty key
    // ARGV[1] = refillPerSecond, ARGV[2] = burst, ARGV[3] = nowMs, ARGV[4] = requestedTokens
    // Returns 1 if granted, 0 if throttled.
    public const string Acquire = @"
if redis.call('EXISTS', KEYS[2]) == 1 then
  return 0
end
local refill = tonumber(ARGV[1])
local burst = tonumber(ARGV[2])
local now = tonumber(ARGV[3])
local requested = tonumber(ARGV[4])
local data = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(data[1])
local ts = tonumber(data[2])
if tokens == nil then
  tokens = burst
  ts = now
end
local elapsed = (now - ts) / 1000.0
if elapsed < 0 then elapsed = 0 end
tokens = math.min(burst, tokens + elapsed * refill)
local granted = 0
if tokens >= requested then
  tokens = tokens - requested
  granted = 1
end
redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now)
redis.call('PEXPIRE', KEYS[1], 3600000)
return granted
";
}
