namespace DotNetRepoInspector.Cli;

public sealed record CliLoggingOptions(CliVerbosity Verbosity)
{
    public static CliLoggingOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verbosity = CliVerbosity.Normal;
        foreach (var argument in args)
        {
            if (string.Equals(argument, "--debug", StringComparison.Ordinal))
            {
                verbosity = CliVerbosity.Debug;
                continue;
            }

            if (verbosity == CliVerbosity.Normal &&
                (string.Equals(argument, "--verbose", StringComparison.Ordinal) ||
                 string.Equals(argument, "-v", StringComparison.Ordinal)))
            {
                verbosity = CliVerbosity.Verbose;
            }
        }

        return new CliLoggingOptions(verbosity);
    }
}
