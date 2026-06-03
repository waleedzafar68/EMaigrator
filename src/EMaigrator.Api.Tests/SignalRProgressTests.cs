using System;
using System.Threading.Tasks;
using EMaigrator.Api.Realtime;
using EMaigrator.Api.Tenancy;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Drives the real <see cref="MigrationsHub"/> over the in-process TestServer: an authenticated client
/// that <c>Subscribe</c>s to a migration it owns receives a <c>Progress</c> push when the
/// <see cref="IMigrationGroupNotifier"/> fans one out, and a cross-tenant <c>Subscribe</c> is rejected.
/// The hub authorizes off the connection's principal (the <c>tenant_id</c> claim) with an explicit
/// tenant predicate — NOT the ambient query filter, which is HttpContext-less (so unfiltered) on the
/// WebSocket transport. The bearer token is passed via the <c>access_token</c> query string the JWT
/// <c>OnMessageReceived</c> handler reads for <c>/hubs</c> paths.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SignalRProgressTests
{
    private readonly ApiInfraFixture _fx;

    public SignalRProgressTests(ApiInfraFixture fx) => _fx = fx;

    [Fact(Timeout = 30_000)]
    public async Task Subscribed_client_receives_progress_push()
    {
        await using var factory = new ApiTestFactory(_fx);
        var (http, tenantId) = await AuthClient.CreateAsync(factory);
        using var _http = http;
        var token = http.DefaultRequestHeaders.Authorization!.Parameter!;

        var migrationId = await SeedJobAsync(factory, tenantId);

        await using var conn = BuildConnection(factory, token);

        MigrationProgressDto? received = null;
        var tcs = new TaskCompletionSource();
        conn.On<MigrationProgressDto>(nameof(IMigrationProgressClient.Progress), dto =>
        {
            received = dto;
            tcs.TrySetResult();
        });

        await conn.StartAsync();
        await conn.InvokeAsync("Subscribe", migrationId.ToString());

        // Act: push via the notifier resolved from DI (in-process, no Redis backplane in tests).
        using (var scope = factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IMigrationGroupNotifier>();
            await notifier.PushProgressAsync(
                new MigrationProgressDto(migrationId.ToString(), 5, 10, "/Inbox", 120, "Running"));
        }

        (await Task.WhenAny(tcs.Task, Task.Delay(5000))).Should().Be(tcs.Task, "progress push should arrive");
        received!.Migrated.Should().Be(5);
    }

    [Fact(Timeout = 30_000)]
    public async Task Subscribe_to_other_tenants_migration_throws()
    {
        await using var factory = new ApiTestFactory(_fx);
        var (httpA, _) = await AuthClient.CreateAsync(factory);
        var (httpB, tenantB) = await AuthClient.CreateAsync(factory);
        using var _httpA = httpA;
        using var _httpB = httpB;

        var migrationB = await SeedJobAsync(factory, tenantB);
        var tokenA = httpA.DefaultRequestHeaders.Authorization!.Parameter!;

        await using var conn = BuildConnection(factory, tokenA);
        await conn.StartAsync();

        var act = async () => await conn.InvokeAsync("Subscribe", migrationB.ToString());
        await act.Should().ThrowAsync<Exception>("cross-tenant subscribe must be rejected");
    }

    private static HubConnection BuildConnection(ApiTestFactory factory, string token) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "hubs/migrations?access_token=" + token),
                o => o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();

    /// <summary>
    /// Seeds a Running Job (+ MailboxMigration) under <paramref name="tenantId"/> and returns the Job id
    /// (the SignalR migration-group key). Sets the seeding scope's tenant so the explicit-tenant insert
    /// is clean; the scoped context is HttpContext-less here anyway, so the ambient filter is inert.
    /// </summary>
    private static async Task<Guid> SeedJobAsync(ApiTestFactory factory, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = (TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.Current = tenantId;

        var db = scope.ServiceProvider.GetRequiredService<EmaigratorDbContext>();
        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceProvider = new ProviderId("imap"),
            DestProvider = new ProviderId("graph"),
            Status = JobStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Jobs.Add(job);
        db.Set<MailboxMigration>().Add(new MailboxMigration
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            SourceMailbox = "a@b.c",
            DestMailbox = "a@d.c",
            Status = MailboxMigrationStatus.Running,
        });
        await db.SaveChangesAsync();
        return job.Id;
    }
}
