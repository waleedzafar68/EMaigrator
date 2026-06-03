using System.Text.Json;
using System.Text.Json.Serialization;

namespace EMaigrator.Cli.Output;

public sealed class JsonOutputWriter(TextWriter sink) : IOutputWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public void WriteConnectTest(ConnectTestOutput output) => sink.WriteLine(JsonSerializer.Serialize(output, Options));
    public void WritePreflight(PreflightOutput output) => sink.WriteLine(JsonSerializer.Serialize(output, Options));
    public void WriteRun(RunOutput output) => sink.WriteLine(JsonSerializer.Serialize(output, Options));
    public void WriteStatus(StatusOutput output) => sink.WriteLine(JsonSerializer.Serialize(output, Options));
    public void WriteError(string message) =>
        sink.WriteLine(JsonSerializer.Serialize(new { error = message }, Options));
}
