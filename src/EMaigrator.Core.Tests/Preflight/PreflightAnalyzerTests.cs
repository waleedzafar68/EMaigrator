using System.Runtime.CompilerServices;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Core.Preflight;

namespace EMaigrator.Core.Tests.Preflight;

public class PreflightAnalyzerTests
{
    private sealed class StubSource : ISourceProvider
    {
        private readonly IReadOnlyList<CanonicalFolder> _folders;
        public StubSource(IReadOnlyList<CanonicalFolder> folders) => _folders = folders;
        public ProviderId Id => new("stub-src");
        public ProviderConstraints Constraints { get; } = new();
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
            => Task.FromResult(new ConnectionTestResult(true, _folders.Count, 0));
        public Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
            => Task.FromResult(_folders);
        public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
            FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield break;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubDest : IDestinationProvider
    {
        public StubDest(ProviderConstraints constraints) => Constraints = constraints;
        public ProviderId Id => new("stub-dst");
        public ProviderConstraints Constraints { get; }
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
            => Task.FromResult(new ConnectionTestResult(true, 0, 0));
        public Task EnsureFolderAsync(FolderPath folder, CancellationToken ct) => Task.CompletedTask;
        public Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct)
            => Task.FromResult(new WriteResult(true));
        public Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
            => Task.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static StubSource Source(params (string path, long count)[] folders)
        => new(folders.Select(f => new CanonicalFolder(FolderPath.Parse(f.path), f.count)).ToList());

    [Fact]
    public async Task Analyze_FlagsFolderTooDeep()
    {
        var src = Source(("A/B/C/D/E", 10));
        var dst = new StubDest(new ProviderConstraints { MaxFolderDepth = 3 });
        var plan = await new PreflightAnalyzer().AnalyzeAsync(src, dst, new ScopeSpec(), CancellationToken.None);

        plan.Issues.Should().ContainSingle(i =>
            i.IssueType == "FolderTooDeep" &&
            i.RecommendedAction == RemediationAction.FlattenFolder &&
            i.Severity == Severity.Warning);
        plan.Issues.Single().AffectedPaths.Should().Contain("A/B/C/D/E");
    }

    [Fact]
    public async Task Analyze_FlagsIllegalFolderName()
    {
        var src = Source(("A:B", 5));
        var dst = new StubDest(new ProviderConstraints { IllegalNameChars = new[] { ':' } });
        var plan = await new PreflightAnalyzer().AnalyzeAsync(src, dst, new ScopeSpec(), CancellationToken.None);
        plan.Issues.Should().ContainSingle(i =>
            i.IssueType == "IllegalFolderName" && i.RecommendedAction == RemediationAction.SanitizeFolderName);
    }

    [Fact]
    public async Task Analyze_FlagsPathTooLong()
    {
        var src = Source(("AAAAAAAAAA/BBBBBBBBBB", 1));
        var dst = new StubDest(new ProviderConstraints { MaxPathLengthChars = 10 });
        var plan = await new PreflightAnalyzer().AnalyzeAsync(src, dst, new ScopeSpec(), CancellationToken.None);
        plan.Issues.Should().Contain(i =>
            i.IssueType == "FolderPathTooLong" && i.RecommendedAction == RemediationAction.RenameFolder);
    }

    [Fact]
    public async Task Analyze_PermissiveDest_NoIssues()
    {
        var src = Source(("Inbox", 100), ("Sent", 50));
        var dst = new StubDest(new ProviderConstraints());
        var plan = await new PreflightAnalyzer().AnalyzeAsync(src, dst, new ScopeSpec(), CancellationToken.None);
        plan.Issues.Should().BeEmpty();
        plan.Estimate.FolderCount.Should().Be(2);
        plan.Estimate.MessageCount.Should().Be(150);
        plan.Estimate.MailboxCount.Should().Be(1);
    }

    [Fact]
    public async Task Analyze_ExcludeFolders_RemovesFromIssuesAndEstimate()
    {
        var src = Source(("Inbox", 100), ("A/B/C/D/E", 10));
        var dst = new StubDest(new ProviderConstraints { MaxFolderDepth = 2 });
        var scope = new ScopeSpec { ExcludeFolders = new[] { "A/B/C/D/E" } };
        var plan = await new PreflightAnalyzer().AnalyzeAsync(src, dst, scope, CancellationToken.None);
        plan.Issues.Should().BeEmpty();
        plan.Estimate.FolderCount.Should().Be(1);
        plan.Estimate.MessageCount.Should().Be(100);
    }

    [Fact]
    public async Task Analyze_IncludeFolders_LimitsScope()
    {
        var src = Source(("Inbox", 100), ("Sent", 50));
        var dst = new StubDest(new ProviderConstraints());
        var scope = new ScopeSpec { IncludeFolders = new[] { "Sent" } };
        var plan = await new PreflightAnalyzer().AnalyzeAsync(src, dst, scope, CancellationToken.None);
        plan.Estimate.FolderCount.Should().Be(1);
        plan.Estimate.MessageCount.Should().Be(50);
    }

    [Fact]
    public async Task Analyze_BatchPairs_SetMailboxCount()
    {
        var src = Source(("Inbox", 1));
        var dst = new StubDest(new ProviderConstraints());
        var scope = new ScopeSpec
        {
            IsBatch = true,
            Pairs = new[] { new MailboxPair("a@o", "a@n"), new MailboxPair("b@o", "b@n") },
        };
        var plan = await new PreflightAnalyzer().AnalyzeAsync(src, dst, scope, CancellationToken.None);
        plan.Estimate.MailboxCount.Should().Be(2);
    }

    [Fact]
    public async Task Analyze_HonorsCancellation()
    {
        var src = Source(("Inbox", 1));
        var dst = new StubDest(new ProviderConstraints());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await new PreflightAnalyzer().AnalyzeAsync(src, dst, new ScopeSpec(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
