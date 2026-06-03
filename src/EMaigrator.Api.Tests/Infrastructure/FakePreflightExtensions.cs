using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Services;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Core.Preflight;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Test doubles for the async-preflight path (Task 7). <see cref="WithFakePreflight"/> is a marker that
/// documents intent at the call site; <see cref="ApiTestFactory"/> ALWAYS calls
/// <see cref="AddFakePreflight"/> from its service-configuration (after <c>AddTestPlugins</c>).
/// <para>
/// <see cref="AddFakePreflight"/> registers a deterministic <see cref="IPreflightAnalyzer"/> (canned plan),
/// an <c>InlineTaskQueue</c> that runs queued work synchronously so the POST completes the analysis before
/// the test issues the GET, and a fake <c>graph</c> destination plugin. The latter is APPENDED (no
/// <c>RemoveAll</c>) so it coexists with the <c>FakeImapPlugin</c> registered by <c>AddTestPlugins</c>:
/// the <c>ReadyToPreflight</c> helper sets <c>to="graph"</c>, so the runner resolves a <c>graph</c> plugin
/// and calls <c>CreateDestination</c>. The fake analyzer ignores the source/dest, so the connectors only
/// need to be creatable + disposable.
/// </para>
/// </summary>
public static class FakePreflightExtensions
{
    public static ApiTestFactory WithFakePreflight(this ApiTestFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory;
    }

    public static void AddFakePreflight(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPreflightAnalyzer, FakeAnalyzer>();
        services.AddSingleton<IBackgroundTaskQueue, InlineTaskQueue>();

        // APPEND (do NOT RemoveAll) so the fake graph destination plugin coexists with the FakeImapPlugin
        // that AddTestPlugins already registered. The runner selects "graph" by descriptor.Provider.
        services.AddSingleton<IProviderPlugin, FakeGraphPlugin>();
    }

    private sealed class FakeAnalyzer : IPreflightAnalyzer
    {
        public Task<PreflightPlan> AnalyzeAsync(ISourceProvider source, IDestinationProvider dest, ScopeSpec scope, CancellationToken ct)
            => Task.FromResult(new PreflightPlan(
                new[]
                {
                    new PreflightIssue(
                        "FolderDepth",
                        new[] { "/A/B/C/D/E" },
                        RemediationAction.FlattenFolder,
                        new[] { RemediationAction.FlattenFolder, RemediationAction.RenameFolder },
                        Severity.Warning,
                        "Folder too deep"),
                },
                new MigrationEstimate(1, 14, 3201, 250_000_000, TimeSpan.FromMinutes(12))));
    }

    // Runs queued work items synchronously against the root provider so the test sees the persisted plan
    // immediately (no hosted pump). Replaces the production BackgroundTaskQueue for IBackgroundTaskQueue.
    private sealed class InlineTaskQueue : IBackgroundTaskQueue
    {
        private readonly IServiceProvider _root;

        public InlineTaskQueue(IServiceProvider root)
        {
            ArgumentNullException.ThrowIfNull(root);
            _root = root;
        }

        public async ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            using var scope = _root.CreateScope();
            await workItem(scope.ServiceProvider, CancellationToken.None);
        }
    }

    /// <summary>
    /// A deterministic destination-only <see cref="IProviderPlugin"/> for provider <c>"graph"</c>. The
    /// runner only needs to create + dispose it (the fake analyzer ignores the dest), so the write paths
    /// are no-ops/not-needed and <c>CreateSource</c> is unsupported.
    /// </summary>
    private sealed class FakeGraphPlugin : IProviderPlugin
    {
        public ProviderId Id => new("graph");

        public IReadOnlyCollection<AuthMethod> SupportedAuth => new[] { AuthMethod.GraphAppOAuth };

        public bool CanBeSource => false;

        public bool CanBeDestination => true;

        public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets) =>
            throw new NotSupportedException("FakeGraphPlugin is destination-only.");

        public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets) =>
            new FakeDestination();

        private sealed class FakeDestination : IDestinationProvider
        {
            public ProviderId Id => new("graph");

            public ProviderConstraints Constraints => new();

            public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) =>
                Task.FromResult(new ConnectionTestResult(true, 0, 0));

            public Task EnsureFolderAsync(FolderPath folder, CancellationToken ct) => Task.CompletedTask;

            public Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct) =>
                throw new NotSupportedException("Preflight never writes.");

            public Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct) =>
                Task.FromResult(false);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
