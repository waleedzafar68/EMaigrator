using System.IO;
using System.Text;

namespace EMaigrator.Connectors.Gmail.Tests;

/// <summary>Captures everything written so tests can assert no credential ever appears in output.</summary>
public sealed class RecordingTextWriter : TextWriter
{
    private readonly StringBuilder _sb = new();
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char value) => _sb.Append(value);
    public override void Write(string? value) => _sb.Append(value);
    public string Captured => _sb.ToString();
}
