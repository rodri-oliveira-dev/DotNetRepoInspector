using System.Text.Json;

using DotNetRepoInspector.Cli;

using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class CliLoggingTests
{
    [Fact]
    public void LoggingOptions_DebugOverridesVerbose()
    {
        var options = CliLoggingOptions.Parse(
            new[] { "--verbose", "--debug" });

        Assert.Equal(CliVerbosity.Debug, options.Verbosity);
    }

    [Fact]
    public void Logger_FiltersVerboseAndDebugByConfiguredVerbosity()
    {
        var error = new StringWriter();
        var logger = new CliLogger(error, CliVerbosity.Verbose);

        logger.Information("inspection.start", "Inspection started.");
        logger.Verbose("inspection.discovery", "Projects were discovered.");
        logger.Debug("inspection.command", "A process command was prepared.");

        var log = error.ToString();
        Assert.Contains("[info]", log, StringComparison.Ordinal);
        Assert.Contains("[verbose]", log, StringComparison.Ordinal);
        Assert.DoesNotContain("[debug]", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Logger_RedactsSensitiveStructuredContext()
    {
        var error = new StringWriter();
        var logger = new CliLogger(error, CliVerbosity.Debug);

        logger.Debug(
            "inspection.context",
            "Safe structured context.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["project"] = "src/App/App.csproj",
                ["accessToken"] = "super-secret-token",
                ["connectionString"] = "Server=secret"
            });

        var log = error.ToString();
        Assert.Contains("project=\"src/App/App.csproj\"", log, StringComparison.Ordinal);
        Assert.Contains("accessToken=\"<redacted>\"", log, StringComparison.Ordinal);
        Assert.Contains("connectionString=\"<redacted>\"", log, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=secret", log, StringComparison.Ordinal);
    }

    [Fact]
    public void CliConsole_KeepsJsonOnStdoutWhenVerboseLogsAreEmitted()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var console = new CliConsole(output, error, CliVerbosity.Verbose);

        console.Logger.Verbose("inspection.discovery", "Discovery completed.");
        console.WriteJson("{\"schemaVersion\":\"1.1\"}");

        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.DoesNotContain("verbose", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[verbose]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Program_DebugDoesNotEchoRawArguments()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = Program.Run(
            new[] { "--debug", "--token=do-not-log-this" },
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("[debug]", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("argumentCount=\"2\"", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-log-this", error.ToString(), StringComparison.Ordinal);
    }
}
