using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Task 9: <c>GET /migrations/{id}/results</c> aggregates per-mailbox <see cref="ILedger"/> counts into a
/// counts + source↔dest reconciliation + needs-decision queue; <c>GET /migrations/{id}/audit</c> projects
/// <see cref="MigrationLogRow"/> into <c>AuditEntryDto[]</c>, omitting the subject when
/// <see cref="Job.StoreSubjects"/> is false (privacy toggle), filterable by <c>?q=</c> and
/// <c>?failuresOnly=</c>; <c>POST /migrations/{id}/rerun</c> re-enqueues each mailbox via the
/// <see cref="IJobOrchestrator"/>. A deterministic <see cref="FakeLedger"/> (3/1/1/0) makes the counts
/// stable without a worker; a <see cref="RecordingOrchestrator"/> captures the rerun enqueue. Ownership is
/// enforced via the tenant-filtered <see cref="EmaigratorDbContext"/> (cross-tenant → 404; the fallback
/// authorization policy rejects anonymous callers, covered by earlier suites).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ResultsAuditTests
{
    private readonly ApiTestFactory _factory;

    public ResultsAuditTests(ApiInfraFixture fx) =>
        _factory = new ApiTestFactory(fx).WithRecordingOrchestrator();

    private async Task<Guid> SeedCompletedJob(Guid tenantId, bool storeSubjects)
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
            StoreSubjects = storeSubjects,
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
            MigratedCount = 3,
            SkippedCount = 1,
            FailedCount = 1,
        };
        db.MailboxMigrations.Add(mbx);

        db.MigrationLogs.Add(new MigrationLogRow
        {
            MailboxMigrationId = mbx.Id,
            Subject = "Re: invoice",
            MessageDate = now,
            SourceFolder = "/Archive",
            DestFolder = "/Archive",
            Status = "Migrated",
            CreatedAt = now,
        });
        db.MigrationLogs.Add(new MigrationLogRow
        {
            MailboxMigrationId = mbx.Id,
            Subject = "Big file",
            MessageDate = now,
            SourceFolder = "/Sent",
            DestFolder = "/Sent",
            Status = "Failed",
            ErrorCode = "SIZE",
            CreatedAt = now,
        });

        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact(Timeout = 30_000)]
    public async Task Results_returns_counts_and_reconciliation()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: true);

        var res = await client.GetFromJsonAsync<JsonElement>($"/api/v1/migrations/{id}/results");

        res.GetProperty("counts").GetProperty("migrated").GetInt64().Should().Be(3);
        res.GetProperty("reconciliation").TryGetProperty("destCount", out _).Should().BeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task Audit_omits_subject_when_store_subjects_false()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: false);

        var arr = await client.GetFromJsonAsync<JsonElement>($"/api/v1/migrations/{id}/audit");

        foreach (var e in arr.EnumerateArray())
        {
            (e.GetProperty("subject").ValueKind == JsonValueKind.Null)
                .Should().BeTrue("privacy toggle hides subjects");
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Audit_failures_only_filter()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: true);

        var arr = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/migrations/{id}/audit?failuresOnly=true");

        arr.GetArrayLength().Should().Be(1);
        arr[0].GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact(Timeout = 30_000)]
    public async Task Rerun_reenqueues_mailboxes()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: true);

        using var res = await client.PostAsync($"/api/v1/migrations/{id}/rerun", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Enqueued.Should().HaveCountGreaterThanOrEqualTo(1);
    }
}
