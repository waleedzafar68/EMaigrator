using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EMaigrator.Cli.Io;

/// <summary>Writes files readable/writable only by the current user (profile &amp; local secrets).</summary>
public static class SecureFile
{
    public static void WriteAllText(string path, string content)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WriteWindows(path, content);
        }
        else
        {
            using (File.Create(path)) { }
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllText(path, content);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteWindows(string path, string content)
    {
        File.WriteAllText(path, content);
        var fi = new FileInfo(path);
        FileSecurity sec = fi.GetAccessControl();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule existing in
                 sec.GetAccessRules(true, true, typeof(NTAccount)).Cast<FileSystemAccessRule>())
        {
            sec.RemoveAccessRule(existing);
        }

        var owner = WindowsIdentity.GetCurrent().User!;
        sec.AddAccessRule(new FileSystemAccessRule(
            owner, FileSystemRights.FullControl, AccessControlType.Allow));
        fi.SetAccessControl(sec);
    }
}
