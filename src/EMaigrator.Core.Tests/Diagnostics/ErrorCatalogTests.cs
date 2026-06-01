using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;

namespace EMaigrator.Core.Tests.Diagnostics;

public class ErrorCatalogTests
{
    private static ErrorRule Rule(string regex, ProviderId? provider = null,
        RemediationKind kind = RemediationKind.Transient, string diagnosis = "diag", string suggestion = "sugg")
        => new()
        {
            Provider = provider,
            SignatureRegex = regex,
            Diagnosis = diagnosis,
            Suggestion = suggestion,
            Kind = kind,
            Severity = Severity.Warning,
            RecommendedAction = RemediationAction.RetryWithBackoff,
            Options = new[] { RemediationAction.RetryWithBackoff },
        };

    [Fact]
    public void Match_ReturnsResolution_OnRegexMatch()
    {
        var catalog = new ErrorCatalog(new[] { Rule("429|throttl") });
        var res = catalog.Match(new ProviderId("graph"), "HTTP 429 throttled by tenant");
        res.Should().NotBeNull();
        res!.Diagnosis.Should().Be("diag");
        res.RecommendedAction.Should().Be(RemediationAction.RetryWithBackoff);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var catalog = new ErrorCatalog(new[] { Rule("MailboxFull") });
        catalog.Match(new ProviderId("graph"), "errorcode=mailboxfull").Should().NotBeNull();
    }

    [Fact]
    public void Match_ReturnsNull_WhenNoRuleMatches()
    {
        var catalog = new ErrorCatalog(new[] { Rule("429") });
        catalog.Match(new ProviderId("graph"), "completely-unknown-condition").Should().BeNull();
    }

    [Fact]
    public void Match_ProviderSpecificOverridesAgnostic()
    {
        var catalog = new ErrorCatalog(new[]
        {
            Rule("quota", provider: null, diagnosis: "generic-quota"),
            Rule("quota", provider: new ProviderId("graph"), diagnosis: "graph-quota"),
        });
        catalog.Match(new ProviderId("graph"), "quota exceeded")!.Diagnosis.Should().Be("graph-quota");
        catalog.Match(new ProviderId("gmail"), "quota exceeded")!.Diagnosis.Should().Be("generic-quota");
    }

    [Fact]
    public void Match_ProviderRuleDoesNotLeakToOtherProvider()
    {
        var catalog = new ErrorCatalog(new[] { Rule("xspecial", provider: new ProviderId("graph")) });
        catalog.Match(new ProviderId("gmail"), "xspecial").Should().BeNull();
    }

    [Fact]
    public void Match_NeverEchoesSignatureText()
    {
        var catalog = new ErrorCatalog(new[] { Rule("badpass") });
        var signatureWithSecret = "AUTH failed for password=Sup3rSecret! (badpass)";
        var res = catalog.Match(new ProviderId("imap"), signatureWithSecret);
        res.Should().NotBeNull();
        res!.Diagnosis.Should().NotContain("Sup3rSecret");
        res.Suggestion.Should().NotContain("Sup3rSecret");
        res.Diagnosis.Should().Be("diag");
        res.Suggestion.Should().Be("sugg");
    }

    [Fact]
    public void Constructor_RejectsInvalidRegex()
    {
        var act = () => new ErrorCatalog(new[] { Rule("(unclosed") });
        act.Should().Throw<ArgumentException>();
    }
}
