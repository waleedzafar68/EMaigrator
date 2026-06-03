namespace EMaigrator.Cli.Secrets;

/// <summary>Reads a secret from the terminal without echoing keystrokes.</summary>
public interface IConsoleSecretReader
{
    string ReadSecret(string promptLabel);
}
