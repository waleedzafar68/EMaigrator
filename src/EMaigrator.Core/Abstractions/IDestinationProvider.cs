using EMaigrator.Core.Model;

namespace EMaigrator.Core.Abstractions;

/// <summary>A write target mailbox (CONTRACTS.md §2).</summary>
public interface IDestinationProvider : IAsyncDisposable
{
    ProviderId Id { get; }
    ProviderConstraints Constraints { get; }
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
    Task EnsureFolderAsync(FolderPath folder, CancellationToken ct);
    Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct);
    Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct);
}
