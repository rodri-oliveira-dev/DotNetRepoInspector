using DotNetRepoInspector.Core.Classification;

namespace DotNetRepoInspector.Cli;

public static class CliOptionsParser
{
    private const string OutputOption = "--output";
    private const string OutputShortOption = "-o";
    private const string ConfigurationOption = "--config";
    private const string ExcludeOption = "--exclude";
    private const string ClassifyOption = "--classify";

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verbosity = CliLoggingOptions.Parse(args).Verbosity;
        var excludedPaths = new List<string>();
        var classificationOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        string? repositoryPath = null;
        string? outputPath = null;
        string? configurationPath = null;
        var disableConfigurationFile = false;
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

            if (string.Equals(argument, "--no-config", StringComparison.Ordinal))
            {
                disableConfigurationFile = true;
                continue;
            }

            if (string.Equals(argument, OutputOption, StringComparison.Ordinal) ||
                string.Equals(argument, OutputShortOption, StringComparison.Ordinal))
            {
                if (outputPath is not null)
                {
                    return CliParseResult.Failure("The output option can only be specified once.");
                }

                if (!TryReadOptionValue(args, ref index, out outputPath))
                {
                    return CliParseResult.Failure("The --output option requires a file path.");
                }

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

            if (string.Equals(argument, ConfigurationOption, StringComparison.Ordinal))
            {
                if (configurationPath is not null)
                {
                    return CliParseResult.Failure("The configuration option can only be specified once.");
                }

                if (!TryReadOptionValue(args, ref index, out configurationPath))
                {
                    return CliParseResult.Failure("The --config option requires a file path.");
                }

                continue;
            }

            if (argument.StartsWith($"{ConfigurationOption}=", StringComparison.Ordinal))
            {
                if (configurationPath is not null)
                {
                    return CliParseResult.Failure("The configuration option can only be specified once.");
                }

                configurationPath = argument[(ConfigurationOption.Length + 1)..];
                if (string.IsNullOrWhiteSpace(configurationPath))
                {
                    return CliParseResult.Failure("The --config option requires a file path.");
                }

                continue;
            }

            if (string.Equals(argument, ExcludeOption, StringComparison.Ordinal))
            {
                if (!TryReadOptionValue(args, ref index, out var excludedPath))
                {
                    return CliParseResult.Failure("The --exclude option requires a relative path.");
                }

                excludedPaths.Add(excludedPath);
                continue;
            }

            if (argument.StartsWith($"{ExcludeOption}=", StringComparison.Ordinal))
            {
                var excludedPath = argument[(ExcludeOption.Length + 1)..];
                if (string.IsNullOrWhiteSpace(excludedPath))
                {
                    return CliParseResult.Failure("The --exclude option requires a relative path.");
                }

                excludedPaths.Add(excludedPath);
                continue;
            }

            if (string.Equals(argument, ClassifyOption, StringComparison.Ordinal))
            {
                if (!TryReadOptionValue(args, ref index, out var classificationOverride) ||
                    !TryAddClassificationOverride(classificationOverride, classificationOverrides))
                {
                    return CliParseResult.Failure(
                        "The --classify option requires a unique '<project-path>=<kind>' value using a supported kind.");
                }

                continue;
            }

            if (argument.StartsWith($"{ClassifyOption}=", StringComparison.Ordinal))
            {
                var classificationOverride = argument[(ClassifyOption.Length + 1)..];
                if (!TryAddClassificationOverride(classificationOverride, classificationOverrides))
                {
                    return CliParseResult.Failure(
                        "The --classify option requires a unique '<project-path>=<kind>' value using a supported kind.");
                }

                continue;
            }

            if (argument.StartsWith('-'))
            {
                return CliParseResult.Failure("An unknown command-line option was provided.");
            }

            if (repositoryPath is not null)
            {
                return CliParseResult.Failure("Only one repository path can be specified.");
            }

            repositoryPath = argument;
        }

        if (disableConfigurationFile && configurationPath is not null)
        {
            return CliParseResult.Failure("The --config and --no-config options cannot be used together.");
        }

        return CliParseResult.Success(new CliOptions(
            repositoryPath ?? ".",
            outputPath,
            verbosity,
            configurationPath,
            disableConfigurationFile,
            excludedPaths.ToArray(),
            classificationOverrides,
            showHelp,
            showVersion));
    }

    private static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryAddClassificationOverride(
        string value,
        Dictionary<string, string> classificationOverrides)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.LastIndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        var projectPath = value[..separatorIndex].Trim();
        var kind = NormalizeClassificationKind(value[(separatorIndex + 1)..]);
        if (string.IsNullOrWhiteSpace(projectPath) ||
            kind is null ||
            classificationOverrides.ContainsKey(projectPath))
        {
            return false;
        }

        classificationOverrides[projectPath] = kind;
        return true;
    }

    private static string? NormalizeClassificationKind(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            ProjectClassificationKinds.Web => ProjectClassificationKinds.Web,
            ProjectClassificationKinds.Worker => ProjectClassificationKinds.Worker,
            ProjectClassificationKinds.Console => ProjectClassificationKinds.Console,
            ProjectClassificationKinds.Library => ProjectClassificationKinds.Library,
            ProjectClassificationKinds.Test => ProjectClassificationKinds.Test,
            ProjectClassificationKinds.Unknown => ProjectClassificationKinds.Unknown,
            _ => null
        };
}
