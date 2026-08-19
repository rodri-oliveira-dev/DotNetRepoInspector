using System.Text.Json;

namespace DotNetRepoInspector.Cli;

public sealed class CliConsole
{
    private readonly TextWriter _standardOutput;

    public CliConsole(
        TextWriter standardOutput,
        TextWriter standardError,
        CliVerbosity verbosity)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        _standardOutput = standardOutput;
        Logger = new CliLogger(standardError, verbosity);
    }

    public CliLogger Logger
    {
        get;
    }

    public void WriteJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        _standardOutput.WriteLine(json.TrimEnd());
    }
}
