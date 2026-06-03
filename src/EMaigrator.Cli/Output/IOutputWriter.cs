namespace EMaigrator.Cli.Output;

public interface IOutputWriter
{
    void WriteConnectTest(ConnectTestOutput output);
    void WritePreflight(PreflightOutput output);
    void WriteRun(RunOutput output);
    void WriteStatus(StatusOutput output);
    void WriteError(string message);
}
