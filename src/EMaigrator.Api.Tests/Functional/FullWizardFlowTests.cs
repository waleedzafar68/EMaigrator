using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Notifications;
using EMaigrator.Api.Realtime;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Api.Tests.Functional;

/// <summary>
/// Functional capstone (Task 14): one authenticated operator drives the WHOLE wizard against the in-process
/// API — register/login → create draft → endpoints → connect both sides → test → scope → preflight →
/// approve (→Running, orchestrator enqueued) → live SignalR completion → one terminal email → results → PDF
/// report — without a 5xx anywhere. The doubles (fake plugins, inline preflight, recording orchestrator,
/// fake ledger, capturing email + stub resolver) are all wired by <see cref="ApiTestFactory"/>; the
/// <c>With*</c> markers below are documentation only.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FullWizardFlowTests
{
    private readonly ApiTestFactory _factory;

    public FullWizardFlowTests(ApiInfraFixture fx) =>
        _factory = new ApiTestFactory(fx)
            .WithFakeImapPlugin().WithFakePreflight().WithRecordingOrchestrator().WithCapturingEmail();

    [Fact(Timeout = 60_000)]
    public async Task Operator_drives_full_migration_end_to_end()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);

        var create = await client.PostAsJsonAsync("/api/v1/migrations", new { });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        (await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from",
            new { auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret = "pw" }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/to",
            new { auth = "ImapBasic", settings = new { host = "h2", port = "993", accountEmail = "d@e.f" }, secret = "pw2" }))
            .IsSuccessStatusCode.Should().BeTrue();

        FakeImapPlugin.CurrentMode = FakeImapPlugin.Mode.Ok;
        var test = await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null);
        (await test.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ok").GetBoolean().Should().BeTrue();

        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/scope",
            new { isBatch = false, pairs = new[] { new { sourceMailbox = "a@b.c", destMailbox = "d@e.f" } } }))
            .IsSuccessStatusCode.Should().BeTrue();

        (await client.PostAsync($"/api/v1/migrations/{id}/preflight", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.GetAsync($"/api/v1/migrations/{id}/preflight")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Subscribe to live progress.
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var conn = new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + "hubs/migrations?access_token=" + token,
                o => o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler()).Build();
        var statusTcs = new TaskCompletionSource<string>();
        conn.On<MigrationProgressDto>(nameof(IMigrationProgressClient.Progress), _ => { });
        conn.On<string, string>(nameof(IMigrationProgressClient.StatusChanged), (mid, st) => { if (st == "Completed") statusTcs.TrySetResult(st); });
        await conn.StartAsync();
        await conn.InvokeAsync("Subscribe", id);

        var approve = await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string> { ["FolderDepth"] = "FlattenFolder" } });
        (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Running");

        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Enqueued.Should().HaveCount(1);

        // Simulate the worker finishing: bridge a terminal progress event for the mailbox + fire the notifier.
        var mailboxId = orch.Enqueued[0];
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IMigrationGroupNotifier>();
            await notifier.PushProgressAsync(new MigrationProgressDto(id, 3201, 3201, null, 0, "Completed"));
            await notifier.PushStatusChangedAsync(id, "Completed");
        }

        (await Task.WhenAny(statusTcs.Task, Task.Delay(5000))).Should().Be(statusTcs.Task, "completed status should fan out over SignalR");
        await conn.DisposeAsync();

        // Drive the email notifier directly (the worker would publish MigrationProgressEvent in production).
        var emailNotifier = new TerminalStateNotifier(
            _factory.Services.GetRequiredService<IAppEmailSender>(),
            _factory.Services.GetRequiredService<INotificationRecipientResolver>(),
            new OneShotSentGuard());
        var ev = NSubstitute.Substitute.For<MassTransit.ConsumeContext<EMaigrator.Core.Contracts.MigrationProgressEvent>>();
        ev.Message.Returns(new EMaigrator.Core.Contracts.MigrationProgressEvent(mailboxId, 3201, 3201, null, 0, "Completed"));
        await emailNotifier.Consume(ev);
        ((CapturingEmailSender)_factory.Services.GetRequiredService<IAppEmailSender>()).Sent.Should().HaveCount(1);

        var results = await client.GetFromJsonAsync<JsonElement>($"/api/v1/migrations/{id}/results");
        results.GetProperty("counts").TryGetProperty("migrated", out _).Should().BeTrue();

        var report = await client.GetAsync($"/api/v1/migrations/{id}/report?format=pdf");
        report.StatusCode.Should().Be(HttpStatusCode.OK);
        report.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }
}

file sealed class OneShotSentGuard : ISentGuard
{
    private bool _done;
    public Task<bool> TryMarkSentAsync(Guid id, CancellationToken ct) { var first = !_done; _done = true; return Task.FromResult(first); }
}
