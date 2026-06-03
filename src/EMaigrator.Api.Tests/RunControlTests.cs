using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Task 8: <c>POST /migrations/{id}/approve</c> persists the per-issue-type resolutions, flips the Job to
/// <c>Running</c> (WizardStep ≥ 5), and enqueues every <c>MailboxMigration</c> via the
/// <see cref="IJobOrchestrator"/>; <c>POST /.../pause|resume|cancel</c> drive the orchestrator and set the
/// matching status. A <see cref="RecordingOrchestrator"/> (singleton) captures the calls. Approve on a Job
/// that is not <c>AwaitingApproval</c> is 409. Tenant scoping + the anonymous-401 fallback are covered by
/// earlier suites.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RunControlTests
{
    private readonly ApiTestFactory _factory;

    public RunControlTests(ApiInfraFixture fx) =>
        _factory = new ApiTestFactory(fx).WithFakeImapPlugin().WithFakePreflight().WithRecordingOrchestrator();

    private static async Task<string> ApprovableMigration(HttpClient c)
    {
        var created = await c.PostAsJsonAsync("/api/v1/migrations", new { });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        await c.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from",
            new { auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret = "pw" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/to",
            new { auth = "ImapBasic", settings = new { host = "h2", port = "993", accountEmail = "d@e.f" }, secret = "pw2" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/scope",
            new { isBatch = false, pairs = new[] { new { sourceMailbox = "a@b.c", destMailbox = "d@e.f" } } });
        await c.PostAsync($"/api/v1/migrations/{id}/preflight", null);
        await c.GetAsync($"/api/v1/migrations/{id}/preflight");   // inline queue → AwaitingApproval
        return id;
    }

    [Fact]
    public async Task Approve_enqueues_and_sets_running()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ApprovableMigration(client);

        using var res = await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string> { ["FolderDepth"] = "FlattenFolder" } });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Running");

        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Enqueued.Should().HaveCount(1);
    }

    [Fact]
    public async Task Approve_when_not_awaiting_returns_409()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var created = await client.PostAsJsonAsync("/api/v1/migrations", new { });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        using var res = await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string>() });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pause_resume_cancel_drive_orchestrator()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ApprovableMigration(client);
        using (await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string>() }))
        {
        }

        (await client.PostAsync($"/api/v1/migrations/{id}/pause", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/migrations/{id}/resume", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/migrations/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Paused.Should().Contain(Guid.Parse(id));
        orch.Resumed.Should().Contain(Guid.Parse(id));
        orch.Cancelled.Should().Contain(Guid.Parse(id));
    }
}
