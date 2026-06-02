using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Sessions;

public sealed class ProviderSessionFactoryTests
{
    private static readonly ProviderId Imap = new("imap");
    private static readonly ProviderId Graph = new("graph");

    private static ConnectionDescriptor Desc(ProviderId p, AuthMethod auth, string? secretRef) => new()
    {
        Provider = p,
        Auth = auth,
        Settings = new Dictionary<string, string> { ["host"] = "mail.example.com" },
        SecretRef = secretRef
    };

    [Fact]
    public async Task Creates_source_from_matching_plugin_and_decrypts_secret()
    {
        var source = Substitute.For<ISourceProvider>();
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(Imap);
        SecretBundle? captured = null;
        plugin.CreateSource(Arg.Any<ConnectionDescriptor>(), Arg.Do<SecretBundle>(b => captured = b))
              .Returns(source);

        var secrets = Substitute.For<ISecretStore>();
        secrets.RetrieveAsync("ref-1", Arg.Any<CancellationToken>())
               .Returns(Task.FromResult("{\"password\":\"hunter2\"}"));

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        var mid = Guid.NewGuid();
        lookup.GetAsync(mid, Arg.Any<CancellationToken>())
              .Returns(new MigrationConnections(Guid.NewGuid(), "tenant-1",
                  Desc(Imap, AuthMethod.ImapBasic, "ref-1"), Desc(Graph, AuthMethod.GraphAppOAuth, null)));

        var factory = new ProviderSessionFactory(new[] { plugin }, secrets, lookup);
        var result = await factory.CreateSourceAsync(mid, CancellationToken.None);

        result.Should().BeSameAs(source);
        captured.Should().NotBeNull();
        captured!.Values.Should().ContainKey("password");
        captured.Values["password"].Should().Be("hunter2");
    }

    [Fact]
    public async Task Destination_with_no_secretref_gets_empty_bundle()
    {
        var dest = Substitute.For<IDestinationProvider>();
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(Graph);
        SecretBundle? captured = null;
        plugin.CreateDestination(Arg.Any<ConnectionDescriptor>(), Arg.Do<SecretBundle>(b => captured = b))
              .Returns(dest);

        var secrets = Substitute.For<ISecretStore>();
        var lookup = Substitute.For<IMigrationConnectionLookup>();
        var mid = Guid.NewGuid();
        lookup.GetAsync(mid, Arg.Any<CancellationToken>())
              .Returns(new MigrationConnections(Guid.NewGuid(), "tenant-1",
                  Desc(Imap, AuthMethod.ImapBasic, "ref-1"), Desc(Graph, AuthMethod.GraphAppOAuth, null)));

        var factory = new ProviderSessionFactory(new[] { plugin }, secrets, lookup);
        var result = await factory.CreateDestinationAsync(mid, CancellationToken.None);

        result.Should().BeSameAs(dest);
        captured!.Values.Should().BeEmpty();
        await secrets.DidNotReceive().RetrieveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_provider_throws()
    {
        var secrets = Substitute.For<ISecretStore>();
        var lookup = Substitute.For<IMigrationConnectionLookup>();
        var mid = Guid.NewGuid();
        lookup.GetAsync(mid, Arg.Any<CancellationToken>())
              .Returns(new MigrationConnections(Guid.NewGuid(), "tenant-1",
                  Desc(Imap, AuthMethod.ImapBasic, null), Desc(Graph, AuthMethod.GraphAppOAuth, null)));

        var factory = new ProviderSessionFactory(Array.Empty<IProviderPlugin>(), secrets, lookup);
        var act = async () => await factory.CreateSourceAsync(mid, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
