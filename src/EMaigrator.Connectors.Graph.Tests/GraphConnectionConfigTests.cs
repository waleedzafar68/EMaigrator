using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphConnectionConfigTests
{
    private const string Secret = "super-secret-client-value-DO-NOT-LOG";

    private static ConnectionDescriptor Descriptor(
        IReadOnlyDictionary<string, string>? settings = null,
        AuthMethod auth = AuthMethod.GraphAppOAuth) => new()
    {
        Provider = new ProviderId("graph"),
        Auth = auth,
        Settings = settings ?? new Dictionary<string, string>
        {
            ["tenantId"] = "11111111-1111-1111-1111-111111111111",
            ["clientId"] = "22222222-2222-2222-2222-222222222222",
            ["accountEmail"] = "user@contoso.onmicrosoft.com",
        },
        SecretRef = "ref-1",
    };

    private static SecretBundle Bundle() =>
        new(new Dictionary<string, string> { ["clientSecret"] = Secret });

    [Fact]
    public void FromDescriptor_extracts_settings_and_secret()
    {
        var cfg = GraphConnectionConfig.FromDescriptor(Descriptor(), Bundle());

        cfg.TenantId.Should().Be("11111111-1111-1111-1111-111111111111");
        cfg.ClientId.Should().Be("22222222-2222-2222-2222-222222222222");
        cfg.AccountEmail.Should().Be("user@contoso.onmicrosoft.com");
        cfg.ClientSecret.Should().Be(Secret);
    }

    [Fact]
    public void GraphScopes_is_least_privilege_default_scope()
    {
        GraphConnectionConfig.GraphScopes.Should().Equal("https://graph.microsoft.com/.default");
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("clientId")]
    [InlineData("accountEmail")]
    public void FromDescriptor_throws_when_a_required_setting_is_missing(string missingKey)
    {
        var settings = new Dictionary<string, string>
        {
            ["tenantId"] = "t",
            ["clientId"] = "c",
            ["accountEmail"] = "a@contoso.com",
        };
        settings.Remove(missingKey);

        var act = () => GraphConnectionConfig.FromDescriptor(Descriptor(settings), Bundle());

        act.Should().Throw<GraphConfigurationException>()
           .Which.Message.Should().Contain(missingKey);
    }

    [Fact]
    public void FromDescriptor_throws_when_client_secret_missing_without_leaking()
    {
        var emptyBundle = new SecretBundle(new Dictionary<string, string>());

        var act = () => GraphConnectionConfig.FromDescriptor(Descriptor(), emptyBundle);

        act.Should().Throw<GraphConfigurationException>()
           .Which.Message.Should().Contain("clientSecret");
    }

    [Fact]
    public void FromDescriptor_throws_for_unsupported_auth_method()
    {
        var act = () => GraphConnectionConfig.FromDescriptor(
            Descriptor(auth: AuthMethod.ImapBasic), Bundle());

        act.Should().Throw<GraphConfigurationException>();
    }

    [Fact]
    public void ToString_never_contains_the_client_secret()
    {
        var cfg = GraphConnectionConfig.FromDescriptor(Descriptor(), Bundle());

        cfg.ToString().Should().NotContain(Secret);
    }
}
