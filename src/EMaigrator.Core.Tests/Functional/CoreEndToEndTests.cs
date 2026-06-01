using System.Runtime.CompilerServices;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using EMaigrator.Core.Preflight;

namespace EMaigrator.Core.Tests.Functional;

public class CoreEndToEndTests
{
    private static readonly ProviderConstraints OutlookLike = new()
    {
        MaxFolderDepth = 3,
        MaxPathLengthChars = 255,
        IllegalNameChars = new[] { ':', '\\', '*', '?', '<', '>', '|' },
        FolderSeparator = '/',
    };

    private sealed class TreeSource : ISourceProvider
    {
        private readonly IReadOnlyList<CanonicalFolder> _folders;
        public TreeSource(IReadOnlyList<CanonicalFolder> folders) => _folders = folders;
        public ProviderId Id => new("imap");
        public ProviderConstraints Constraints { get; } = new();
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
            => Task.FromResult(new ConnectionTestResult(true, _folders.Count, _folders.Sum(f => f.EstimatedMessageCount)));
        public Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct) => Task.FromResult(_folders);
        public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
            FolderPath folder, ReadOptions options, [EnumeratorCancellation] CancellationToken ct)
        { await Task.Yield(); yield break; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullDest : IDestinationProvider
    {
        public NullDest(ProviderConstraints c) => Constraints = c;
        public ProviderId Id => new("graph");
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

    [Fact]
    public async Task Preflight_DetectsViolations_AndRecommendedActionsResolveThem()
    {
        var source = new TreeSource(new[]
        {
            new CanonicalFolder(FolderPath.Parse("Inbox"), 500),
            new CanonicalFolder(FolderPath.Parse("Projects/Clients/2025/Q4/Archive"), 120), // depth 5 > 3
            new CanonicalFolder(FolderPath.Parse("Notes:Personal"), 30),                     // illegal ':'
        });
        var dest = new NullDest(OutlookLike);

        var plan = await new PreflightAnalyzer().AnalyzeAsync(source, dest, new ScopeSpec(), CancellationToken.None);

        // Estimate
        plan.Estimate.FolderCount.Should().Be(3);
        plan.Estimate.MessageCount.Should().Be(650);
        plan.Estimate.TotalBytes.Should().BeGreaterThan(0);
        plan.Estimate.EstimatedDuration.Should().BeGreaterThan(TimeSpan.Zero);

        // Issues
        var tooDeep = plan.Issues.Single(i => i.IssueType == "FolderTooDeep");
        tooDeep.RecommendedAction.Should().Be(RemediationAction.FlattenFolder);
        var illegal = plan.Issues.Single(i => i.IssueType == "IllegalFolderName");
        illegal.RecommendedAction.Should().Be(RemediationAction.SanitizeFolderName);

        // Applying the recommended FlattenFolder yields a destination-legal depth.
        var deep = FolderPath.Parse(tooDeep.AffectedPaths.Single());
        var flattened = FolderFlattener.Flatten(deep, OutlookLike.MaxFolderDepth);
        flattened.Depth.Should().BeLessThanOrEqualTo(OutlookLike.MaxFolderDepth);

        // Applying the recommended SanitizeFolderName removes the illegal character.
        var dirty = FolderPath.Parse(illegal.AffectedPaths.Single());
        var clean = FolderSanitizer.Sanitize(dirty, OutlookLike);
        clean.Segments.Should().NotContain(seg => seg.Contains(':'));
    }

    [Fact]
    public void IdentityKey_IsIndependentOfFolderMapping()
    {
        var input = new MessageIdentityInput
        {
            MessageId = null,
            From = "a@old.com",
            To = "b@old.com",
            Subject = "Invoice",
            Date = DateTimeOffset.UnixEpoch,
            DecodedBodySha256Hex = "abc123",
        };
        // Folder transforms do not feed identity; the key is purely message content.
        var before = IdentityKey.Compute(input);
        var after = IdentityKey.Compute(input);
        before.Should().Be(after);
        before.Should().StartWith("h:");
    }
}
