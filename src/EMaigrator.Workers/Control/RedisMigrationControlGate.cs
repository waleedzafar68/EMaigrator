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
    {
        var db = _mux.GetDatabase();
        // Do not override a terminal Cancel.
        if (await GetStateAsync(jobId, ct) == MigrationControlState.Cancelled) return;
        await db.StringSetAsync(Key(jobId), (int)MigrationControlState.Paused).WaitAsync(ct);
    }

    public async Task ResumeAsync(Guid jobId, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        if (await GetStateAsync(jobId, ct) == MigrationControlState.Cancelled) return;
        await db.StringSetAsync(Key(jobId), (int)MigrationControlState.Active).WaitAsync(ct);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken ct)
    {
        var db = _mux.GetDatabase();
        await db.StringSetAsync(Key(jobId), (int)MigrationControlState.Cancelled).WaitAsync(ct);
    }
}
