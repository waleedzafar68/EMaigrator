using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Retention;

/// <summary>Deletes MigrationLog rows older than the configured retention window (default 30 days).</summary>
public sealed partial class LogRetentionPurgeService : BackgroundService
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;
    private readonly RetentionOptions _options;
    private readonly ILogger<LogRetentionPurgeService> _logger;

    public LogRetentionPurgeService(
        IDbContextFactory<EmaigratorDbContext> factory,
        IOptions<RetentionOptions> options,
        ILogger<LogRetentionPurgeService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> PurgeOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-_options.LogRetentionDays);
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var deleted = await ctx.MigrationLogs
            .Where(r => r.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        if (deleted > 0)
        {
            LogPurged(deleted, cutoff);
        }

        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await PurgeOnceAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // Background loop must survive transient failures and keep running.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogPurgeFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Purged {Count} migration log rows older than {Cutoff}")]
    private partial void LogPurged(int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Log retention purge failed")]
    private partial void LogPurgeFailed(Exception ex);
}
