using EMaigrator.Connectors.Gmail;
using FluentAssertions;

namespace EMaigrator.Connectors.Gmail.Tests;

public class ProjectReferenceGuardTests
{
    [Fact]
    public void GmailAssembly_ReferencesOnlyCoreAndGoogleAndFramework()
    {
        var asm = typeof(AssemblyMarker).Assembly;
        var referenced = asm.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        string[] forbidden =
        {
            "EMaigrator.Infrastructure",
            "EMaigrator.Workers",
            "EMaigrator.Api",
            "EMaigrator.Cli",
            "EMaigrator.Connectors.Imap",
            "EMaigrator.Connectors.Graph",
        };

        referenced.Should().NotContain(forbidden);
        referenced.Should().Contain("EMaigrator.Core");
    }
}
