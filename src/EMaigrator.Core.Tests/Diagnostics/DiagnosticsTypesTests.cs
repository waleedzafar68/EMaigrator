using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Core.Tests.Diagnostics;

public class DiagnosticsTypesTests
{
    [Fact]
    public void RemediationAction_HasExactMembers()
        => Enum.GetNames<RemediationAction>().Should().BeEquivalentTo(
            "None", "RetryWithBackoff", "FlattenFolder", "SanitizeFolderName",
            "RenameFolder", "MergeFolder", "SkipMessage");

    [Fact]
    public void Severity_And_Kind_HaveExactMembers()
    {
        Enum.GetNames<Severity>().Should().BeEquivalentTo("Info", "Warning", "Blocker");
        Enum.GetNames<RemediationKind>().Should().BeEquivalentTo("Transient", "Structural");
    }

    [Fact]
    public void ErrorRule_DefaultsAndRequireds()
    {
        var rule = new ErrorRule
        {
            SignatureRegex = "throttle",
            Diagnosis = "Throttled",
            Suggestion = "Retry later",
            Kind = RemediationKind.Transient,
            Severity = Severity.Warning,
        };
        rule.Provider.Should().BeNull();
        rule.RecommendedAction.Should().Be(RemediationAction.None);
        rule.Options.Should().BeEmpty();
        rule.HelpUrl.Should().BeNull();
    }

    [Fact]
    public void ErrorResolution_Constructs()
    {
        var rule = new ErrorRule
        {
            SignatureRegex = "x", Diagnosis = "d", Suggestion = "s",
            Kind = RemediationKind.Structural, Severity = Severity.Blocker,
        };
        var res = new ErrorResolution(rule, "d", "s", RemediationKind.Structural,
            RemediationAction.FlattenFolder, new[] { RemediationAction.FlattenFolder }, Severity.Blocker);
        res.Diagnosis.Should().Be("d");
        res.RecommendedAction.Should().Be(RemediationAction.FlattenFolder);
    }
}
