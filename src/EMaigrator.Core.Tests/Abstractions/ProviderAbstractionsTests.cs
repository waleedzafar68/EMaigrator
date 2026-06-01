using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Tests.Abstractions;

public class ProviderAbstractionsTests
{
    private sealed class FakeProvider : ISourceProvider, IDestinationProvider
    {
        public ProviderId Id => new("fake");
        public ProviderConstraints Constraints { get; } = new();
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
            => Task.FromResult(new ConnectionTestResult(true, 1, 1));
        public Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CanonicalFolder>>(new[] { new CanonicalFolder(FolderPath.Parse("Inbox"), 1) });
        public async IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(
            FolderPath folder, ReadOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return new CanonicalMessage
            {
                IdentityKey = "mid:<x@y>",
                InternalDate = DateTimeOffset.UnixEpoch,
                OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream()),
            };
        }
        public Task EnsureFolderAsync(FolderPath folder, CancellationToken ct) => Task.CompletedTask;
        public Task<WriteResult> WriteMessageAsync(FolderPath folder, CanonicalMessage message, CancellationToken ct)
            => Task.FromResult(new WriteResult(true, "dest-id"));
        public Task<bool> ExistsByMessageIdAsync(FolderPath folder, string messageId, CancellationToken ct)
            => Task.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void AuthMethod_HasExactMembers()
    {
        Enum.GetNames<AuthMethod>().Should().BeEquivalentTo(
            "ImapBasic", "ImapOAuthXoauth2", "GraphAppOAuth", "GraphDelegatedOAuth",
            "GmailServiceAccountDwd", "GmailDelegatedOAuth");
    }

    [Fact]
    public void ProviderConstraints_DefaultsArePermissive()
    {
        var c = new ProviderConstraints();
        c.MaxFolderDepth.Should().Be(int.MaxValue);
        c.MaxPathLengthChars.Should().Be(int.MaxValue);
        c.MaxMessageBytes.Should().Be(long.MaxValue);
        c.MaxAttachmentBytes.Should().Be(long.MaxValue);
        c.FolderSeparator.Should().Be('/');
        c.IllegalNameChars.Should().BeEmpty();
        c.ReservedFolderNames.Should().BeEmpty();
    }

    [Fact]
    public async Task FakeProvider_RoundTripsCanonicalTypes()
    {
        await using ISourceProvider src = new FakeProvider();
        await using IDestinationProvider dst = new FakeProvider();

        var folders = await src.ListFoldersAsync(CancellationToken.None);
        folders.Should().ContainSingle(f => f.Path.Name == "Inbox");

        await foreach (var m in src.ReadMessagesAsync(FolderPath.Parse("Inbox"), new ReadOptions(), CancellationToken.None))
        {
            await dst.EnsureFolderAsync(FolderPath.Parse("Inbox"), CancellationToken.None);
            var result = await dst.WriteMessageAsync(FolderPath.Parse("Inbox"), m, CancellationToken.None);
            result.Written.Should().BeTrue();
            result.DestMessageId.Should().Be("dest-id");
        }
    }

    [Fact]
    public void SecretBundle_ExposesValues()
    {
        var b = new SecretBundle(new Dictionary<string, string> { ["password"] = "p" });
        b.Values["password"].Should().Be("p");
    }

    [Fact]
    public void ConnectionDescriptor_HoldsNonSecretSettingsAndSecretRef()
    {
        var d = new ConnectionDescriptor
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "mail.example.com" },
            SecretRef = "secret-123",
        };
        d.Settings["host"].Should().Be("mail.example.com");
        d.SecretRef.Should().Be("secret-123");
    }
}
