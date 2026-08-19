namespace DotNetRepoInspector.Cli;

public static class CliHelp
{
    public const string Text = """
DotNetRepoInspector

Usage:
  dotnet repo-inspect [path] [options]

Arguments:
  path                  Repository path to inspect. Defaults to the current directory.

Options:
  -o, --output <file>   Write the inspection JSON to a file instead of stdout.
  -v, --verbose         Emit verbose operational logs to stderr.
      --debug           Emit debug operational logs to stderr.
  -h, --help            Show this help text.
      --version         Show the CLI version.

Examples:
  dotnet repo-inspect .
  dotnet repo-inspect ../repository --output inspection.json
  dotnet repo-inspect . --verbose > inspection.json

Exit codes:
  0    Inspection completed without error diagnostics.
  1    Inspection completed and produced a report containing error diagnostics.
  2    Command-line arguments are invalid.
  3    Inspection failed before a report could be produced.
  4    The report could not be written to the requested destination.
  130  The operation was cancelled.
""";
}
