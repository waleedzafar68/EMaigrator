using System.Runtime.InteropServices;
using EMaigrator.Cli.Io;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Io;

public class SecureFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-securefile").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Writes_content_and_restricts_to_owner_only()
    {
        string path = Path.Combine(_dir, "secret-profile.json");

        SecureFile.WriteAllText(path, "{\"hello\":\"world\"}");

        File.ReadAllText(path).Should().Be("{\"hello\":\"world\"}");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            UnixFileMode groupOther =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            (mode & groupOther).Should().Be(UnixFileMode.None, "group/other must have no access");
            (mode & UnixFileMode.UserRead).Should().Be(UnixFileMode.UserRead);
            (mode & UnixFileMode.UserWrite).Should().Be(UnixFileMode.UserWrite);
        }
        else
        {
            var fi = new FileInfo(path);
            var sec = fi.GetAccessControl();
            var rules = sec.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
            foreach (System.Security.AccessControl.FileSystemAccessRule r in rules)
            {
                string id = r.IdentityReference.Value;
                id.Should().NotContainEquivalentOf("everyone")
                    .And.NotContainEquivalentOf("users")
                    .And.NotContainEquivalentOf("authenticated");
            }
        }
    }
}
