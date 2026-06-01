using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests;

public class CiScriptTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "scripts", "check-vulnerable.ps1")))
            dir = dir.Parent;
        dir.Should().NotBeNull("scripts/check-vulnerable.ps1 must exist above the test bin dir");
        return dir!.FullName;
    }

    private static int RunCheck(string listOutput)
    {
        var root = RepoRoot();
        var script = Path.Combine(root, "scripts", "check-vulnerable.ps1");
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, listOutput);
        try
        {
            var psi = new ProcessStartInfo("pwsh",
                $"-NoProfile -File \"{script}\" -InputFile \"{tmp}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Clean_Output_Returns_Zero()
    {
        const string clean = "The given project `EMaigrator.Core` has no vulnerable packages given the current sources.";
        RunCheck(clean).Should().Be(0);
    }

    [Fact]
    public void Vulnerable_Output_Returns_NonZero()
    {
        const string vulnerable =
            "Project `EMaigrator.Api` has the following vulnerable packages\n" +
            "   [net10.0]:\n" +
            "   Top-level Package      Requested   Resolved   Severity   Advisory URL\n" +
            "   > SomePackage          1.0.0       1.0.0      High       https://github.com/advisories/GHSA-xxxx\n";
        RunCheck(vulnerable).Should().NotBe(0);
    }
}
