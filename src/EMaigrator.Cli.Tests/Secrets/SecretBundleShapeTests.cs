using System.Text.Json;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Secrets;

public class SecretBundleShapeTests
{
    [Theory]
    [InlineData(AuthMethod.ImapBasic, "password")]
    [InlineData(AuthMethod.ImapOAuthXoauth2, "accessToken")]
    [InlineData(AuthMethod.GraphAppOAuth, "clientSecret")]
    [InlineData(AuthMethod.GmailServiceAccountDwd, "serviceAccountJson")]
    public void Wraps_raw_under_connector_key(AuthMethod auth, string expectedKey)
    {
        string json = SecretBundleShape.ForAuth(auth, "RAWVAL");
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        dict.Should().ContainKey(expectedKey);
        dict[expectedKey].Should().Be("RAWVAL");
    }
}
