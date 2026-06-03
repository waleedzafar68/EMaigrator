using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Deterministic <see cref="ILedger"/> test double for the results/reconciliation suite (Task 9):
/// <see cref="GetCountsAsync"/> always returns 3 migrated, 1 skipped, 1 failed, 0 pending so the
/// <c>GET /migrations/{id}/results</c> aggregation is stable without a worker writing real ledger rows.
/// The write/scan methods are inert (the API never writes the ledger). Registered as a <b>singleton</b>
/// (see <see cref="FakeLedgerExtensions.AddFakeLedger"/>) replacing the production scoped
/// <c>PostgresLedger</c>.
/// </summary>
public sealed class FakeLedger : ILedger
{
    public Task<bool> IsDoneAsync(Guid mailboxMigrationId, string identityKey, CancellationToken ct) =>
        Task.FromResult(false);

    public Task MarkAsync(
        Guid mailboxMigrationId, string identityKey, string sourceFolder, string destFolder,
        LedgerStatus status, string? errorCode, CancellationToken ct) => Task.CompletedTask;

    public async IAsyncEnumerable<LedgerEntry> GetNotDoneAsync(
        Guid mailboxMigrationId, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<LedgerCounts> GetCountsAsync(Guid mailboxMigrationId, CancellationToken ct) =>
        Task.FromResult(new LedgerCounts(3, 1, 1, 0));
}

/// <summary>
/// Wires the <see cref="FakeLedger"/> into the test host. <see cref="WithFakeLedger"/> is a call-site
/// marker; <see cref="ApiTestFactory"/> ALWAYS calls <see cref="AddFakeLedger"/>.
/// </summary>
public static class FakeLedgerExtensions
{
    public static ApiTestFactory WithFakeLedger(this ApiTestFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory;
    }

    /// <summary>
    /// REMOVE the production <see cref="ILedger"/> (registered scoped as <c>PostgresLedger</c> by
    /// <c>AddInfrastructure</c>) then register the fake as a singleton: <c>RemoveAll</c> guarantees the
    /// deterministic ledger is the only registration the results endpoint resolves.
    /// </summary>
    public static void AddFakeLedger(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<ILedger>();
        services.AddSingleton<ILedger, FakeLedger>();
    }
}
