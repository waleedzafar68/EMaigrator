using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace EMaigrator.Workers.Control;

public sealed class RedisMigrationControlGate : IMigrationControlGate
{
    private readonly IConnectionMultiplexer _mux;

    public RedisMigrationControlGate(IConnectionMultiplexer mux) => _mux = mux;

    private static RedisKey Key(Guid jobId) => $"emaigrator:control:{jobId:N}";

    // Atomically set the state to ARGV[1] ONLY when the job is not already in the terminal Cancelled
    // state (ARGV[2]). Redis executes the script atomically, so a concurrent CancelAsync cannot
    // interleave between the GET and the SET — a Cancel can never be overwritten by an in-flight
    // Pause/Resume (the check-then-set TOCTOU race is eliminated).
    private const string SetUnlessCancelledScript =
        "if redis.call('GET', KEYS[1]) == ARGV[2] then return 0 end\n" +
        "redis.call('SET', KEYS[1], ARGV[1])\n" +
        "return 1";

    public async Task<MigrationControlState> GetStateAsync(Guid jobId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = _mux.GetDatabase();
        var value = await db.StringGetAsync(Key(jobId)).WaitAsync(ct);
        if (value.IsNullOrEmpty)
            return MigrationControlState.Active;
        return (MigrationControlState)(int)value;
    }

    public async Task PauseAsync(Guid jobId, CancellationToken ct)
        => await SetUnlessCancelledAsync(jobId, MigrationControlState.Paused, ct);

    public async Task ResumeAsync(Guid jobId, CancellationToken ct)
        => await SetUnlessCancelledAsync(jobId, MigrationControlState.Active, ct);

    private async Task SetUnlessCancelledAsync(Guid jobId, MigrationControlState target, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        await db.ScriptEvaluateAsync(
            SetUnlessCancelledScript,
            new[] { Key(jobId) },
            new RedisValue[] { (int)target, (int)MigrationControlState.Cancelled })
            .WaitAsync(ct);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        await db.StringSetAsync(Key(jobId), (int)MigrationControlState.Cancelled).WaitAsync(ct);
    }
}
