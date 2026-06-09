using System.Collections.Generic;
using System.Text.Json;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Secrets;
using FluentAssertions;

namespace EMaigrator.Infrastructure.Tests.Secrets;

/// <summary>
/// The shared <see cref="SecretBundleShape"/> is the single source of truth that keeps the API
/// connect-test / preflight, the worker run path, and the CLI all storing & resolving a credential
/// under the SAME connector key. A drift here passes fake-backed tests but fails the real run, so the
/// round-trip (Wrap → Unwrap) and the per-auth key mapping are pinned here.
/// </summary>
public class SecretBundleShapeTests
{
    [Theory]
    [InlineData(AuthMethod.ImapBasic, "password")]
    [InlineData(AuthMethod.ImapOAuthXoauth2, "accessToken")]
    [InlineData(AuthMethod.GraphAppOAuth, "clientSecret")]
    [InlineData(AuthMethod.GraphDelegatedOAuth, "clientSecret")]
    [InlineData(AuthMethod.GmailServiceAccountDwd, "serviceAccountJson")]
    public void Wrap_stores_raw_under_the_connector_key(AuthMethod auth, string expectedKey)
    {
        var json = SecretBundleShape.Wrap(auth, "RAWVAL");

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        dict.Should().ContainKey(expectedKey);
        dict[expectedKey].Should().Be("RAWVAL");
    }

    [Fact]
    public void Wrap_then_Unwrap_round_trips_to_the_same_dictionary()
    {
        // The whole point: what Wrap writes is exactly what Unwrap (used by every read path) gets back.
        var json = SecretBundleShape.Wrap(AuthMethod.GraphAppOAuth, "shh");

        var back = SecretBundleShape.Unwrap(json);

        back.Should().ContainKey("clientSecret").WhoseValue.Should().Be("shh");
    }

    [Fact]
    public void Unwrap_of_blank_or_garbage_is_an_empty_bundle_not_a_throw()
    {
        SecretBundleShape.Unwrap("null").Should().BeEmpty();
    }

    [Fact]
    public void Wrap_preserves_a_json_valued_service_account_blob()
    {
        // Gmail's "raw" is the entire SA key-file (itself JSON) stored as a string value — must survive intact.
        const string saJson = "{\"type\":\"service_account\",\"private_key\":\"-----BEGIN-----\"}";

        var back = SecretBundleShape.Unwrap(SecretBundleShape.Wrap(AuthMethod.GmailServiceAccountDwd, saJson));

        back["serviceAccountJson"].Should().Be(saJson);
    }
}
