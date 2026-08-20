namespace DotNetRepoInspector.Cli;

public static class CliHelp
{
    public const string Text = """
DotNetRepoInspector

Usage:
  dotnet repo-inspect [path] [options]

Arguments:
  path                         Repository path to inspect. Defaults to the current directory.

Options:
  -o, --output <file>          Write the inspection JSON to a file instead of stdout.
      --config <file>          Use a repository-relative configuration file.
      --no-config              Ignore the default .dotnetrepoinspector.json file.
      --exclude <path>         Exclude a repository-relative directory or project. Repeatable.
      --classify <path>=<kind> Override one project classification. Repeatable.
      --sink http              Persist a snapshot through the built-in HTTP/webhook sink.
      --sink-url <url>         HTTP/HTTPS endpoint used by the selected sink.
      --sink-timeout-seconds   Overall persistence timeout in seconds. Default: 15.
      --sink-failure-mode      Persistence failure mode: non-fatal or fatal. Default: non-fatal.
      --sink-max-attempts      Maximum HTTP attempts for transient failures. Default: 3.
  -v, --verbose                Emit verbose operational logs to stderr.
      --debug                  Emit debug operational logs to stderr.
  -h, --help                   Show this help text.
      --version                Show the CLI version.

Classification kinds:
  web, worker, console, library, test, unknown

HTTP sink credentials:
  Set DOTNET_REPO_INSPECTOR_HTTP_TOKEN to send an Authorization: Bearer header.
  Never pass credentials in command-line arguments or embed them in --sink-url.

Examples:
  dotnet repo-inspect .
  dotnet repo-inspect ../repository --output inspection.json
  dotnet repo-inspect . --exclude generated --exclude samples/Legacy.csproj
  dotnet repo-inspect . --classify src/App/App.csproj=web
  dotnet repo-inspect . --sink http --sink-url https://evidence.example/snapshots
  dotnet repo-inspect . --sink http --sink-url https://evidence.example/snapshots --sink-failure-mode fatal
  dotnet repo-inspect . --verbose > inspection.json

Exit codes:
  0    Inspection completed without error diagnostics.
  1    Inspection completed and produced a report containing error diagnostics.
  2    Command-line arguments are invalid.
  3    Inspection failed before a report could be produced.
  4    The report could not be written to the requested destination.
  5    Persistence failed while configured in fatal mode.
  130  The operation was cancelled.
""";
}
