using EMaigrator.Core.Idempotency;
using NetArchTest.Rules;

namespace EMaigrator.Core.Tests.Architecture;

public class CoreDependencyTests
{
    private static readonly System.Reflection.Assembly CoreAssembly = typeof(IdentityKey).Assembly;

    [Fact]
    public void Core_DoesNotDependOnSiblingProjects()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "EMaigrator.Infrastructure",
                "EMaigrator.Connectors",
                "EMaigrator.Workers",
                "EMaigrator.Api",
                "EMaigrator.Cli")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Core must reference nothing in the solution (DESIGN.md §15). Offenders: "
                + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Core_DoesNotDependOnInfrastructureLibraries()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "MassTransit",
                "Microsoft.AspNetCore",
                "Microsoft.Graph",
                "Google.Apis",
                "MailKit",
                "StackExchange.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Core is pure logic with no I/O dependencies. Offenders: "
                + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Core_OnlyReferencesBclAssemblies()
    {
        var referenced = CoreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToList();

        referenced.Should().OnlyContain(name =>
            name.StartsWith("System", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            name == "netstandard" ||
            name == "mscorlib",
            because: "Core must have zero third-party runtime dependencies. References: "
                + string.Join(", ", referenced));
    }
}
