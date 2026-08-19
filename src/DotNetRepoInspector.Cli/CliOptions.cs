namespace DotNetRepoInspector.Cli;

public sealed record CliOptions(
    string RepositoryPath,
    string? OutputPath,
    CliVerbosity Verbosity,
    bool ShowHelp,
    bool ShowVersion);

public sealed record CliParseResult(CliOptions? Options, string? Error)
{
    public bool Succeeded => Options is not null;

    public static CliParseResult Success(CliOptions options) => new(options, null);

    public static CliParseResult Failure(string error) => new(null, error);
}
