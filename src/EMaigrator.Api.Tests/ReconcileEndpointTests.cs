using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EMaigrator.Api.Tenancy;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Task 7: <c>POST /migrations/{id}/reconcile</c> marks the job <see cref="JobMode.Reconcile"/> + Running and
/// enqueues a <c>ReconcileMailbox</c> per mailbox via the <see cref="IJobOrchestrator"/> seam (captured by the
/// <see cref="RecordingOrchestrator"/>). Ownership is enforced via the tenant-filtered context
/// (cross-tenant → 404); the fallback authorization policy rejects anonymous callers (401).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReconcileEndpointTests
{
    private readonly ApiTestFactory _factory;

    public ReconcileEndpointTests(ApiInfraFixture fx) =>
        _factory = new ApiTestFactory(fx).WithRecordingOrchestrator();

    private async Task<(Guid JobId, Guid MailboxId)> SeedJob(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        ((TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>()).Current = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<EmaigratorDbContext>();

        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceProvider = new ProviderId("imap"),
            DestProvider = new ProviderId("graph"),
            Status = JobStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Jobs.Add(job);

        var mbx = new MailboxMigration
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            SourceMailbox = "a@b.c",
            DestMailbox = "d@e.f",
            Status = MailboxMigrationStatus.Completed,
        };
        db.MailboxMigrations.Add(mbx);

        await db.SaveChangesAsync();
        return (job.Id, mbx.Id);
    }

    [Fact(Timeout = 30_000)]
    public async Task Reconcile_sets_mode_running_and_enqueues_per_mailbox()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var (jobId, mailboxId) = await SeedJob(tenantId);

        using var res = await client.PostAsync($"/api/v1/migrations/{jobId}/reconcile", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Reconciled.Should().Contain(mailboxId);

        using var scope = _factory.Services.CreateScope();
        ((TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>()).Current = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<EmaigratorDbContext>();
        var job = db.Jobs.Single(j => j.Id == jobId);
        job.Mode.Should().Be(JobMode.Reconcile);
        job.Status.Should().Be(JobStatus.Running);
    }

    [Fact(Timeout = 30_000)]
    public async Task Reconcile_cross_tenant_id_returns_404()
    {
        var (_, ownerTenant) = await AuthClient.CreateAsync(_factory);
        var (jobId, _) = await SeedJob(ownerTenant);

        var (other, _) = await AuthClient.CreateAsync(_factory);
        using var post = await other.PostAsync($"/api/v1/migrations/{jobId}/reconcile", null);

        post.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = 30_000)]
    public async Task Reconcile_anonymous_returns_401()
    {
        var (_, tenantId) = await AuthClient.CreateAsync(_factory);
        var (jobId, _) = await SeedJob(tenantId);

        var anon = _factory.CreateClient();
        using var post = await anon.PostAsync($"/api/v1/migrations/{jobId}/reconcile", null);

        post.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
