using EMaigrator.Core.Model;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Workers.IntegrationTests;

/// <summary>
/// Task 6: the mode-agnostic <see cref="EfJobStatusFinalizer"/> rolls a job's status to terminal only when
/// ALL of its mailboxes are terminal (NOT on a single ledger's Pending==0 — which would regress the
/// resume-completion race), is idempotent (a second call over an already-terminal job is a no-op), and
/// rolls up to Partial when any mailbox failed.
/// </summary>
[Collection("pipeline")]
public class EfJobStatusFinalizerTests
{
    private readonly EmaigratorPipelineFixture _fx;
    public EfJobStatusFinalizerTests(EmaigratorPipelineFixture fx) => _fx = fx;

    private IDbContextFactory<EmaigratorDbContext> Factory()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_fx.BuildConfiguration(), registerBus: false);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
    }

    private static async Task<(Guid JobId, Guid MailboxId)> SeedAsync(
        IDbContextFactory<EmaigratorDbContext> f, JobStatus jobStatus, params MailboxMigrationStatus[] mailboxStatuses)
    {
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var ctx = await f.CreateDbContextAsync();
        ctx.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = Guid.NewGuid(),
            SourceProvider = new ProviderId("imap"),
            DestProvider = new ProviderId("graph"),
            Status = jobStatus,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var firstMailbox = Guid.Empty;
        foreach (var status in mailboxStatuses)
        {
            var mid = Guid.NewGuid();
            if (firstMailbox == Guid.Empty)
            {
                firstMailbox = mid;
            }

            ctx.MailboxMigrations.Add(new MailboxMigration
            {
                Id = mid,
                JobId = jobId,
                SourceMailbox = "s",
                DestMailbox = "d",
                Status = status,
            });
        }

        await ctx.SaveChangesAsync();
        return (jobId, firstMailbox);
    }

    private static async Task<JobStatus> ReadJobStatusAsync(IDbContextFactory<EmaigratorDbContext> f, Guid jobId)
    {
        await using var ctx = await f.CreateDbContextAsync();
        return (await ctx.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobId)).Status;
    }

    [Fact]
    public async Task Finalizes_to_completed_when_all_mailboxes_terminal_and_is_idempotent()
    {
        var f = Factory();
        var (jobId, mailboxId) = await SeedAsync(f, JobStatus.Running, MailboxMigrationStatus.Completed);
        var finalizer = new EfJobStatusFinalizer(f);

        var status = await finalizer.FinalizeIfDoneAsync(mailboxId, CancellationToken.None);
        status.Should().Be(JobStatus.Completed);
        (await ReadJobStatusAsync(f, jobId)).Should().Be(JobStatus.Completed);

        // Second call → job already terminal → no-op (returns null, breaks any re-publish cycle).
        var second = await finalizer.FinalizeIfDoneAsync(mailboxId, CancellationToken.None);
        second.Should().BeNull();
        (await ReadJobStatusAsync(f, jobId)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task Returns_null_and_leaves_job_running_when_a_mailbox_not_terminal()
    {
        var f = Factory();
        var (jobId, mailboxId) = await SeedAsync(f, JobStatus.Running,
            MailboxMigrationStatus.Completed, MailboxMigrationStatus.Running);
        var finalizer = new EfJobStatusFinalizer(f);

        var status = await finalizer.FinalizeIfDoneAsync(mailboxId, CancellationToken.None);

        status.Should().BeNull();
        (await ReadJobStatusAsync(f, jobId)).Should().Be(JobStatus.Running);
    }

    [Fact]
    public async Task Rolls_up_to_partial_when_a_mailbox_failed()
    {
        var f = Factory();
        var (jobId, mailboxId) = await SeedAsync(f, JobStatus.Running,
            MailboxMigrationStatus.Completed, MailboxMigrationStatus.Failed);
        var finalizer = new EfJobStatusFinalizer(f);

        var status = await finalizer.FinalizeIfDoneAsync(mailboxId, CancellationToken.None);

        status.Should().Be(JobStatus.Partial);
        (await ReadJobStatusAsync(f, jobId)).Should().Be(JobStatus.Partial);
    }
}
