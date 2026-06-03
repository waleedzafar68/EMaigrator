using System;
using System.Threading.Tasks;
using EMaigrator.Api.Tenancy;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Proves the DbContext-level global query filter confines tenant-scoped reads to the caller's
/// tenant: a Job written under tenant B is invisible to a context resolved under tenant A. The
/// per-request scoped <see cref="EmaigratorDbContext"/> reads its tenant from <see cref="ICurrentTenant"/>
/// at creation, so each scope sets <see cref="TestCurrentTenant.Current"/> before resolving the context.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenancyFilterTests
{
    private readonly ApiInfraFixture _fx;

    public TenancyFilterTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Job_of_other_tenant_is_invisible_under_query_filter()
    {
        await using var factory = new ApiTestFactory(_fx);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Guid jobBId;

        // Scope 1: seed a Job under tenant B.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = (TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.Current = tenantB;

            var db = scope.ServiceProvider.GetRequiredService<EmaigratorDbContext>();
            var job = new Job
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"),
                Status = JobStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            jobBId = job.Id;
        }

        // Scope 2: under tenant A, the tenant-B job is filtered out.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = (TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.Current = tenantA;

            var db = scope.ServiceProvider.GetRequiredService<EmaigratorDbContext>();
            var found = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobBId);

            found.Should().BeNull("a job belonging to tenant B must be invisible to tenant A under the global query filter");
        }
    }
}
