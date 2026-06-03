using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// A deterministic <see cref="IProviderPlugin"/> for provider <c>"imap"</c> used by the connection-test
/// tests so they never reach a real IMAP server. <see cref="CurrentMode"/> selects whether the source's
/// <c>TestConnectionAsync</c> succeeds (folderCount 14 / messageCount 3201) or throws the connector's
/// normalized auth-failure signature (<c>imap:AUTHENTICATIONFAILED</c>) that the service maps via the
/// error catalog. The test substitutes this for the real ImapProviderPlugin (see <c>AddTestPlugins</c>).
/// </summary>
public sealed class FakeImapPlugin : IProviderPlugin
{
    public enum Mode { Ok, AuthFail }

    public static Mode CurrentMode { get; set; } = Mode.Ok;

    public ProviderId Id => new("imap");

    public IReadOnlyCollection<AuthMethod> SupportedAuth =>
        new[] { AuthMethod.ImapBasic, AuthMethod.ImapOAuthXoauth2 };

    public bool CanBeSource => true;

    public bool CanBeDestination => true;

    public ISourceProvider CreateSource(ConnectionDescriptor descriptor, SecretBundle secrets) => new FakeSource();

    public IDestinationProvider CreateDestination(ConnectionDescriptor descriptor, SecretBundle secrets) =>
        throw new NotSupportedException("FakeImapPlugin is source-only.");

    private sealed class FakeSource : ISourceProvider
    {
        public ProviderId Id => new("imap");

        public ProviderConstraints Constraints => new();

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) => CurrentMode switch
        {
            Mode.Ok => Task.FromResult(new ConnectionTestResult(true, 14, 3201)),
            // Raw provider failure: the connector normalizes to this signature; the service maps it via the catalog.
            _ => throw new InvalidOperationException("imap:AUTHENTICATIONFAILED"),
        };

        public Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CanonicalFolder>>(Array.Empty<CanonicalFolder>());

        public IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(FolderPath folder, ReadOptions options, CancellationToken ct) =>
            EmptyAsyncEnumerable.Empty<CanonicalMessage>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class EmptyAsyncEnumerable
{
    public static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
