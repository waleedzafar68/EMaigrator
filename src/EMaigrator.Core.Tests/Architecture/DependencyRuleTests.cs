using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace EMaigrator.Core.Tests.Architecture;

public class DependencyRuleTests
{
    private const string CoreAssembly = "EMaigrator.Core";

    private static readonly string[] ForbiddenForCore =
    [
        "EMaigrator.Infrastructure",
        "EMaigrator.Connectors.Imap",
        "EMaigrator.Connectors.Graph",
        "EMaigrator.Connectors.Gmail",
        "EMaigrator.Workers",
        "EMaigrator.Api",
        "EMaigrator.Cli",
    ];

    [Fact]
    public void Core_DoesNotDependOn_AnyHigherLayer()
    {
        var coreAsm = typeof(EMaigrator.Core.AssemblyMarker).Assembly;

        var result = Types.InAssembly(coreAsm)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenForCore)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "EMaigrator.Core must reference nothing (DESIGN.md §15). Offending types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
