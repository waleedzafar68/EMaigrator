using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Core.Tests.Security;

public class SecretsHygieneTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".gitignore")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    [Fact]
    public void Gitignore_Covers_Secret_Patterns()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepoRoot(), ".gitignore"));
        foreach (var pattern in new[] { ".env", ".env.*", "*.pem", "secrets.json", "appsettings.*.local.json" })
            gitignore.Should().Contain(pattern, $".gitignore must ignore {pattern}");
    }

    [Fact]
    public void No_Secret_Files_Are_Tracked()
    {
        var root = RepoRoot();
        var psi = new ProcessStartInfo("git", "ls-files")
        { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = root };
        using var p = Process.Start(psi)!;
        var tracked = p.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        p.WaitForExit();

        var offenders = tracked.Where(f =>
            (f.EndsWith(".pem", StringComparison.Ordinal)) ||
            (f.EndsWith("secrets.json", StringComparison.Ordinal)) ||
            (Path.GetFileName(f) == ".env") ||
            (Path.GetFileName(f).StartsWith(".env.", StringComparison.Ordinal) && Path.GetFileName(f) != ".env.example") ||
            (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^appsettings\..*\.local\.json$"))
        ).ToList();

        offenders.Should().BeEmpty("no secret files may be committed; found: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EnvExample_Has_No_Real_Secrets()
    {
        var example = Path.Combine(RepoRoot(), "deploy", ".env.example");
        File.Exists(example).Should().BeTrue();
        var text = File.ReadAllText(example);
        // Placeholder convention: passwords are "change-me", never a real value.
        text.Should().Contain("POSTGRES_PASSWORD=change-me");
        text.Should().Contain("RABBITMQ_PASSWORD=change-me");
    }
}
