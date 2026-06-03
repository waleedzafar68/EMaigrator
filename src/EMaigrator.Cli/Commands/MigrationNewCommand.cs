using EMaigrator.Cli.Io;

namespace EMaigrator.Cli.Commands;

public static class MigrationNewCommand
{
    private const string Template = """
    {
      "tenantId": "self-host",
      "storeSubjects": false,
      "from": {
        "provider": "imap",
        "auth": "ImapBasic",
        "settings": { "host": "imap.workmail.example.com", "port": "993", "accountEmail": "user@source.example.com" }
      },
      "to": {
        "provider": "imap",
        "auth": "ImapBasic",
        "settings": { "host": "imap.dest.example.com", "port": "993", "accountEmail": "user@dest.example.com" }
      },
      "scope": {
        "isBatch": false,
        "pairs": [ { "sourceMailbox": "user@source.example.com", "destMailbox": "user@dest.example.com" } ]
      }
    }
    """;

    public static CliExitCode Execute(string path, bool force)
    {
        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine($"Refusing to overwrite existing profile '{path}'. Use --force to replace it.");
            return CliExitCode.ConfigError;
        }

        try
        {
            SecureFile.WriteAllText(path, Template);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write profile: {ex.Message}");
            return CliExitCode.ConfigError;
        }

        Console.Error.WriteLine($"Created starter profile at '{path}' (owner-only permissions). " +
                                "Pass secrets at run time via EMAIGRATOR_SECRET_FROM/_TO or the prompt.");
        return CliExitCode.Success;
    }
}
