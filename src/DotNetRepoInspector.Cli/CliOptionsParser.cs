namespace DotNetRepoInspector.Cli;

public static class CliOptionsParser
{
    private const string OutputOption = "--output";
    private const string OutputShortOption = "-o";

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verbosity = CliLoggingOptions.Parse(args).Verbosity;
        string? repositoryPath = null;
        string? outputPath = null;
        var showHelp = false;
        var showVersion = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.IsNullOrWhiteSpace(argument))
            {
                return CliParseResult.Failure("Command-line arguments cannot contain empty values.");
            }

            if (string.Equals(argument, "--help", StringComparison.Ordinal) ||
                string.Equals(argument, "-h", StringComparison.Ordinal))
            {
                showHelp = true;
                continue;
            }

            if (string.Equals(argument, "--version", StringComparison.Ordinal))
            {
                showVersion = true;
                continue;
            }

            if (string.Equals(argument, "--verbose", StringComparison.Ordinal) ||
                string.Equals(argument, "-v", StringComparison.Ordinal) ||
                string.Equals(argument, "--debug", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(argument, OutputOption, StringComparison.Ordinal) ||
                string.Equals(argument, OutputShortOption, StringComparison.Ordinal))
            {
                if (outputPath is not null)
                {
                    return CliParseResult.Failure("The output option can only be specified once.");
                }

                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    return CliParseResult.Failure("The --output option requires a file path.");
                }

                outputPath = args[++index];
                continue;
            }

            if (argument.StartsWith($"{OutputOption}=", StringComparison.Ordinal))
            {
                if (outputPath is not null)
                {
                    return CliParseResult.Failure("The output option can only be specified once.");
                }

                outputPath = argument[(OutputOption.Length + 1)..];
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return CliParseResult.Failure("The --output option requires a file path.");
                }

                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                return CliParseResult.Failure("An unknown command-line option was provided.");
            }

            if (repositoryPath is not null)
            {
                return CliParseResult.Failure("Only one repository path can be specified.");
            }

            repositoryPath = argument;
        }

        return CliParseResult.Success(new CliOptions(
            repositoryPath ?? ".",
            outputPath,
            verbosity,
            showHelp,
            showVersion));
    }
}
