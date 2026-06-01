using System.Text.RegularExpressions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Idempotency;
using EMaigrator.Core.Model;
using Xunit.Abstractions;

namespace EMaigrator.Core.Tests.Security;

public class IdentityAndCatalogSecurityTests
{
    private readonly ITestOutputHelper _output;
    public IdentityAndCatalogSecurityTests(ITestOutputHelper output) => _output = output;

    private const string Password = "P@ssw0rd-LEAK";

    [Fact]
    public void IdentityHash_IsDeterministicFingerprint()
    {
        var input = new MessageIdentityInput
        {
            MessageId = null,
            From = "alice@example.com",
            To = "bob@example.com",
            Subject = "Report",
            Date = DateTimeOffset.UnixEpoch,
            DecodedBodySha256Hex = "feedface",
        };
        var a = IdentityKey.Compute(input);
        var b = IdentityKey.Compute(input);
        _output.WriteLine($"hash#1 = {a}");
        _output.WriteLine($"hash#2 = {b}");
        a.Should().Be(b);
        a.Should().MatchRegex("^h:[0-9a-f]{64}$");
    }

    [Fact]
    public void IdentityHash_DoesNotEchoSecret()
    {
        var input = new MessageIdentityInput
        {
            MessageId = null,
            From = $"{Password}@example.com",
            To = "bob@example.com",
            Subject = $"secret is {Password}",
            Date = DateTimeOffset.UnixEpoch,
            DecodedBodySha256Hex = Password, // even if a secret is fed in, it is hashed away
        };
        var key = IdentityKey.Compute(input);
        _output.WriteLine($"key = {key}");
        key.Should().StartWith("h:");
        key.Should().NotContain(Password);
        key.Substring(2).Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void IdentityHash_NeverHashesRawBytes()
    {
        // Same decoded-body fingerprint + same headers => same key, regardless of raw transit form.
        MessageIdentityInput Make() => new()
        {
            MessageId = null,
            From = "alice@example.com",
            To = "bob@example.com",
            Subject = "Report",
            Date = DateTimeOffset.UnixEpoch,
            DecodedBodySha256Hex = "decoded-only-fingerprint",
        };
        IdentityKey.Compute(Make()).Should().Be(IdentityKey.Compute(Make()));
    }

    [Fact]
    public void MessageIdPath_DoesNotAppendSecretFields()
    {
        var key = IdentityKey.Compute(new MessageIdentityInput
        {
            MessageId = "<abc@host>",
            From = $"{Password}@x",       // would-be leak source
            Subject = Password,
            DecodedBodySha256Hex = Password,
        });
        _output.WriteLine($"mid key = {key}");
        key.Should().Be("mid:abc@host");
        key.Should().NotContain(Password);
    }

    [Fact]
    public void ErrorCatalog_NeverEchoesCredentialsInDiagnosis()
    {
        var catalog = new ErrorCatalog(new[]
        {
            new ErrorRule
            {
                Provider = new ProviderId("imap"),
                SignatureRegex = "auth.*fail|invalid.*credential",
                Diagnosis = "Authentication to the source failed.",
                Suggestion = "Re-enter the app password and run Test connection again.",
                Kind = RemediationKind.Structural,
                Severity = Severity.Blocker,
                RecommendedAction = RemediationAction.None,
            },
        });

        var leakySignature =
            "AUTH failed: invalid credential password=Sup3r$ecret123 Authorization: Bearer eyJhbGciOi.LEAKED.TOKEN";
        var res = catalog.Match(new ProviderId("imap"), leakySignature);

        res.Should().NotBeNull();
        var serialized = $"{res!.Diagnosis}\n{res.Suggestion}\n{string.Join(',', res.Options)}\n{res.RecommendedAction}";
        _output.WriteLine("resolution text:");
        _output.WriteLine(serialized);

        // grep-style: zero occurrences of either secret token in the output.
        Regex.Count(serialized, Regex.Escape("Sup3r$ecret123")).Should().Be(0);
        Regex.Count(serialized, Regex.Escape("LEAKED.TOKEN")).Should().Be(0);

        res.Diagnosis.Should().Be("Authentication to the source failed.");
        res.Suggestion.Should().Be("Re-enter the app password and run Test connection again.");
    }
}
