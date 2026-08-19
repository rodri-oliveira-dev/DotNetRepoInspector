using System.Globalization;

namespace DotNetRepoInspector.Cli;

public static class Program
{
    public static int Main(string[] args) =>
        Run(args, Console.Out, Console.Error);

    public static int Run(
        string[] args,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        var loggingOptions = CliLoggingOptions.Parse(args);
        var console = new CliConsole(
            standardOutput,
            standardError,
            loggingOptions.Verbosity);

        console.Logger.Verbose(
            "cli.verbose-enabled",
            "Verbose operational logging is enabled.");
        console.Logger.Debug(
            "cli.arguments-parsed",
            "Command-line arguments were parsed without logging their values.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["argumentCount"] = args.Length.ToString(CultureInfo.InvariantCulture)
            });

        return 0;
    }
}
