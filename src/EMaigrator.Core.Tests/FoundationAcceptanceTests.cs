using System.Reflection;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests;

public class FoundationAcceptanceTests
{
    private static readonly string[] ProductionAssemblies =
    [
        "EMaigrator.Core",
        "EMaigrator.Connectors.Imap",
        "EMaigrator.Connectors.Graph",
        "EMaigrator.Connectors.Gmail",
        "EMaigrator.Infrastructure",
        "EMaigrator.Workers",
        "EMaigrator.Api",
        "emaigrator", // EMaigrator.Cli sets <AssemblyName>emaigrator</AssemblyName> (the CLI binary name).
    ];

    [Fact]
    public void All_Expected_Assemblies_Load()
    {
        foreach (var name in ProductionAssemblies)
        {
            var act = () => Assembly.Load(new AssemblyName(name));
            act.Should().NotThrow($"{name} must be a loadable assembly in the solution");
        }
    }
}
