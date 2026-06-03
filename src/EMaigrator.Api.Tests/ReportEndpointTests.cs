using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using EMaigrator.Api.Tenancy;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Task 10: <c>GET /migrations/{id}/report?format=csv|pdf</c> streams a downloadable migration report —
/// counts, duration, and a per-folder breakdown — built from a completed <see cref="Job"/>'s
/// <see cref="MailboxMigration"/> totals and <see cref="MigrationLogRow"/> rows (grouped by destination
/// folder). CSV is produced via CsvHelper, PDF via QuestPDF. An unsupported <c>?format=</c> → 400. Ownership
/// is enforced via the tenant-filtered <see cref="EmaigratorDbContext"/> (cross-tenant id → 404; the fallback
/// authorization policy rejects anonymous callers, covered by earlier suites).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReportEndpointTests
{
    private readonly ApiTestFactory _factory;

    public ReportEndpointTests(ApiInfraFixture fx) =>
        _factory = new ApiTestFactory(fx).WithRecordingOrchestrator();

    private async Task<Guid> Seed(Guid tenantId)
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
            MigratedCount = 3180,
            SkippedCount = 18,
            FailedCount = 3,
            StartedAt = now.AddMinutes(-12),
            FinishedAt = now,
        };
        db.MailboxMigrations.Add(mbx);

        db.MigrationLogs.Add(new MigrationLogRow
        {
            MailboxMigrationId = mbx.Id,
            MessageDate = now,
            SourceFolder = "/Archive",
            DestFolder = "/Archive",
            Status = "Migrated",
            CreatedAt = now,
        });

        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact(Timeout = 30_000)]
    public async Task Csv_report_has_headers_and_attachment_disposition()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await Seed(tenantId);

        using var res = await client.GetAsync(new Uri($"/api/v1/migrations/{id}/report?format=csv", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        res.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("Folder,Migrated,Skipped,Failed");
    }

    [Fact(Timeout = 30_000)]
    public async Task Pdf_report_has_pdf_magic()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await Seed(tenantId);

        using var res = await client.GetAsync(new Uri($"/api/v1/migrations/{id}/report?format=pdf", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact(Timeout = 30_000)]
    public async Task Unsupported_format_returns_400()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await Seed(tenantId);

        using var res = await client.GetAsync(new Uri($"/api/v1/migrations/{id}/report?format=xml", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(Timeout = 30_000)]
    public async Task Report_for_other_tenants_job_returns_404()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var otherTenantId = await Seed(Guid.NewGuid());

        using var res = await client.GetAsync(new Uri($"/api/v1/migrations/{otherTenantId}/report?format=csv", UriKind.Relative));

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
