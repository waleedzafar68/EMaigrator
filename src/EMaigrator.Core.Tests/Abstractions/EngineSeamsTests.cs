using System.Runtime.CompilerServices;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Tests.Abstractions;

public class EngineSeamsTests
{
    private sealed class FakeLedger : ILedger
    {
        private readonly Dictionary<(Guid, string), LedgerEntry> _store = new();

        public Task<bool> IsDoneAsync(Guid id, string key, CancellationToken ct)
            => Task.FromResult(_store.TryGetValue((id, key), out var e)
                && e.Status is LedgerStatus.Migrated or LedgerStatus.Skipped);

        public Task MarkAsync(Guid id, string key, string src, string dst, LedgerStatus status, string? err, CancellationToken ct)
        {
            _store[(id, key)] = new LedgerEntry(id, key, src, dst, status, err, DateTimeOffset.UnixEpoch);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<LedgerEntry> GetNotDoneAsync(Guid id, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            foreach (var e in _store.Values.Where(e => e.MailboxMigrationId == id
                && e.Status is LedgerStatus.Pending or LedgerStatus.Failed))
                yield return e;
        }

        public Task<LedgerCounts> GetCountsAsync(Guid id, CancellationToken ct)
        {
            var es = _store.Values.Where(e => e.MailboxMigrationId == id).ToList();
            return Task.FromResult(new LedgerCounts(
                es.Count(e => e.Status == LedgerStatus.Migrated),
                es.Count(e => e.Status == LedgerStatus.Skipped),
                es.Count(e => e.Status == LedgerStatus.Failed),
                es.Count(e => e.Status == LedgerStatus.Pending)));
        }

        public Task SeedPendingAsync(Guid id,
            IEnumerable<(string IdentityKey, string SourceFolder, string DestFolder)> messages, CancellationToken ct)
        {
            foreach (var (key, src, dst) in messages)
            {
                // Insert-if-absent; never downgrade an already-recorded row.
                if (!_store.ContainsKey((id, key)))
                {
                    _store[(id, key)] = new LedgerEntry(id, key, src, dst, LedgerStatus.Pending, null, DateTimeOffset.UnixEpoch);
                }
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public void LedgerStatus_HasExactMembers()
        => Enum.GetNames<LedgerStatus>().Should().BeEquivalentTo("Pending", "Migrated", "Skipped", "Failed");

    [Fact]
    public async Task FakeLedger_RoundTrips()
    {
        var id = Guid.NewGuid();
        ILedger ledger = new FakeLedger();

        (await ledger.IsDoneAsync(id, "mid:<a>", CancellationToken.None)).Should().BeFalse();
        await ledger.MarkAsync(id, "mid:<a>", "Inbox", "Inbox", LedgerStatus.Migrated, null, CancellationToken.None);
        await ledger.MarkAsync(id, "mid:<b>", "Inbox", "Inbox", LedgerStatus.Failed, "ERR", CancellationToken.None);

        (await ledger.IsDoneAsync(id, "mid:<a>", CancellationToken.None)).Should().BeTrue();

        var notDone = new List<LedgerEntry>();
        await foreach (var e in ledger.GetNotDoneAsync(id, CancellationToken.None))
            notDone.Add(e);
        notDone.Should().ContainSingle(e => e.IdentityKey == "mid:<b>" && e.ErrorCode == "ERR");

        var counts = await ledger.GetCountsAsync(id, CancellationToken.None);
        counts.Migrated.Should().Be(1);
        counts.Failed.Should().Be(1);
    }

    [Fact]
    public void RateLimitKey_IsValueType()
    {
        var k = new RateLimitKey(new ProviderId("graph"), "tenant@x.com");
        k.Should().Be(new RateLimitKey(new ProviderId("graph"), "tenant@x.com"));
        typeof(RateLimitKey).IsValueType.Should().BeTrue();
    }
}
