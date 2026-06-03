using System.Text;

namespace EMaigrator.Cli.Secrets;

/// <summary>Default no-echo reader: masks input, supports backspace, never writes the value back.</summary>
public sealed class ConsoleSecretReader : IConsoleSecretReader
{
    public string ReadSecret(string promptLabel)
    {
        Console.Error.Write($"{promptLabel}: ");
        var sb = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true); // intercept = do not echo
            if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
        return sb.ToString();
    }
}
