using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Sessions;

public class ImapMessageSeamTests
{
    private static CanonicalMessage Msg(string id) => new()
    {
        IdentityKey = id,
        InternalDate = DateTimeOffset.UnixEpoch,
        OpenContentAsync = _ => Task.FromResult<Stream>(Stream.Null),
    };

    private static ISourceProvider SourceWith(params CanonicalMessage[] msgs)
    {
        var src = Substitute.For<ISourceProvider>();
        src.ReadMessagesAsync(Arg.Any<FolderPath>(), Arg.Any<ReadOptions>(), Arg.Any<CancellationToken>())
           .Returns(_ => ToAsync(msgs));
        return src;
    }

    private static async IAsyncEnumerable<CanonicalMessage> ToAsync(CanonicalMessage[] msgs)
    {
        foreach (var m in msgs)
        {
            yield return m;
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Lister_yields_identity_keys_in_order()
    {
        var lister = new ImapMessageRefLister();
        var refs = new List<string>();
        await foreach (var r in lister.ListRefsAsync(SourceWith(Msg("a"), Msg("b")), FolderPath.Parse("INBOX"), CancellationToken.None))
        {
            refs.Add(r);
        }

        refs.Should().Equal("a", "b");
    }

    [Fact]
    public async Task Hydrator_returns_matching_message()
    {
        var hydrator = new ImapMessageHydrator();
        var m = await hydrator.HydrateAsync(SourceWith(Msg("a"), Msg("b")), FolderPath.Parse("INBOX"), "b", CancellationToken.None);
        m.IdentityKey.Should().Be("b");
    }

    [Fact]
    public async Task Hydrator_throws_when_reference_absent()
    {
        var hydrator = new ImapMessageHydrator();
        var act = async () => await hydrator.HydrateAsync(SourceWith(Msg("a")), FolderPath.Parse("INBOX"), "zzz", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
