using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Workers.Control;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace EMaigrator.Workers.IntegrationTests.Control;

public sealed class RedisMigrationControlGateTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();
    private ConnectionMultiplexer _mux = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _mux.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private RedisMigrationControlGate Gate() => new(_mux);

    [Fact]
    public async Task Unknown_job_is_active()
    {
        var gate = Gate();
        var state = await gate.GetStateAsync(Guid.NewGuid(), CancellationToken.None);
        state.Should().Be(MigrationControlState.Active);
    }

    [Fact]
    public async Task Pause_then_resume_roundtrips()
    {
        var gate = Gate();
        var job = Guid.NewGuid();
        await gate.PauseAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Paused);
        await gate.ResumeAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Active);
    }

    [Fact]
    public async Task Cancel_is_terminal_and_survives_resume()
    {
        var gate = Gate();
        var job = Guid.NewGuid();
        await gate.CancelAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Cancelled);
        await gate.ResumeAsync(job, CancellationToken.None);
        (await gate.GetStateAsync(job, CancellationToken.None)).Should().Be(MigrationControlState.Cancelled);
    }
}
