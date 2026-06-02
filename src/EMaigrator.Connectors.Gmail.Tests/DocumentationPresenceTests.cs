using System.IO;
using FluentAssertions;

namespace EMaigrator.Connectors.Gmail.Tests;

/// <summary>
/// Guards that the deferred paid-Workspace live-testing risk (DESIGN.md §17) stays documented:
/// the doc must exist and carry the load-bearing marker substrings asserted below. Mirrors the
/// repo-root walk used by <c>EMaigrator.Core.Tests.Security.VulnerabilityGateTests.RepoRoot()</c>
/// so the doc is located regardless of the test's bin directory.
/// </summary>
public class DocumentationPresenceTests
{
    private static string RepoRoot()
    {
        // Walk up from the test's bin directory to the repo root, anchored on a file/folder that
        // only exists there: the supply-chain audit script, or (fallback) the top-level docs folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
            && !File.Exists(Path.Combine(dir.FullName, "scripts", "check-vulnerable.ps1"))
            && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test must run from within the repo tree so the docs folder is reachable");
        return dir!.FullName;
    }

    [Fact]
    public void GmailTestingDoc_ExistsAndDocumentsDeferredRiskAndScope()
    {
        var path = Path.Combine(RepoRoot(), "docs", "connectors", "gmail-testing.md");
        File.Exists(path).Should().BeTrue($"expected the deferred live-testing risk doc at {path}");

        var text = File.ReadAllText(path);
        text.Should().Contain("recorded fixtures", "the doc must state the connector is validated only against recorded fixtures");
        text.Should().Contain("https://mail.google.com/", "the doc must record the single minimal OAuth scope");
    }
}
