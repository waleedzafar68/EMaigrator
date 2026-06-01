using EMaigrator.Core.Model;

namespace EMaigrator.Core.Abstractions;

/// <summary>A read-only mailbox source (CONTRACTS.md §2).</summary>
public interface ISourceProvider : IAsyncDisposable
{
    ProviderId Id { get; }
    ProviderConstraints Constraints { get; }
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
    Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct);
    IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(FolderPath folder, ReadOptions options, CancellationToken ct);
}
