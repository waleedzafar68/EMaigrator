using System;
using EMaigrator.Api.Mapping;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Pure-unit coverage for <see cref="MigrationMapper"/>'s <c>mode</c> projection: <see cref="JobMode"/>
/// maps to the lowercase wire string the SPA's mode-branched wizard binds to (CONTRACTS.md §6).
/// </summary>
public sealed class MigrationMapperTests
{
    private static Job NewJob() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        SourceProvider = new ProviderId("imap"),
        DestProvider = new ProviderId("graph"),
        Status = JobStatus.Draft,
        WizardStep = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void ToDto_maps_reconcile_mode()
    {
        var job = NewJob();
        job.Mode = JobMode.Reconcile;
        MigrationMapper.ToDto(job, Array.Empty<MailboxMigration>()).Mode.Should().Be("reconcile");
    }

    [Fact]
    public void ToDto_defaults_to_migrate_mode()
    {
        var job = NewJob();
        job.Mode = JobMode.Migrate;
        MigrationMapper.ToDto(job, Array.Empty<MailboxMigration>()).Mode.Should().Be("migrate");
    }
}
