using System.Globalization;

using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Persistence;

namespace DotNetRepoInspector.Cli;

public static class CliOptionsParser
{
    private const string OutputOption = "--output";
    private const string OutputShortOption = "-o";
    private const string ConfigurationOption = "--config";
    private const string ExcludeOption = "--exclude";
    private const string ClassifyOption = "--classify";
    private const string SinkOption = "--sink";
    private const string SinkUrlOption = "--sink-url";
    private const string SinkTimeoutOption = "--sink-timeout-seconds";
    private const string SinkFailureModeOption = "--sink-failure-mode";
    private const string SinkMaxAttemptsOption = "--sink-max-attempts";

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verbosity = CliLoggingOptions.Parse(args).Verbosity;
        var excludedPaths = new List<string>();
        var classificationOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        string? repositoryPath = null;
        string? outputPath = null;
        string? configurationPath = null;
        string? sink = null;
        string? sinkUrl = null;
        var sinkTimeoutSeconds = CliPersistenceOptions.DefaultTimeoutSeconds;
        var sinkFailureMode = PersistenceFailureMode.NonFatal;
        var sinkMaxAttempts = CliPersistenceOptions.DefaultMaxAttempts;
        var disableConfigurationFile = false;
        var showHelp = false;
        var showVersion = false;
        var sinkUrlSpecified = false;
        var sinkTimeoutSpecified = false;
        var sinkFailureModeSpecified = false;
        var sinkMaxAttemptsSpecified = false;

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

            if (string.Equals(argument, SinkOption, StringComparison.Ordinal))
            {
                if (sink is not null)
                {
                    return CliParseResult.Failure("The sink option can only be specified once.");
                }

                if (!TryReadOptionValue(args, ref index, out sink))
                {
                    return CliParseResult.Failure("The --sink option requires a sink name.");
                }

                sink = sink.Trim().ToLowerInvariant();
                continue;
            }

            if (argument.StartsWith($"{SinkOption}=", StringComparison.Ordinal))
            {
                if (sink is not null)
                {
                    return CliParseResult.Failure("The sink option can only be specified once.");
                }

                sink = argument[(SinkOption.Length + 1)..].Trim().ToLowerInvariant();
                if (sink.Length == 0)
                {
                    return CliParseResult.Failure("The --sink option requires a sink name.");
                }

                continue;
            }

            if (string.Equals(argument, SinkUrlOption, StringComparison.Ordinal))
            {
                if (sinkUrlSpecified)
                {
                    return CliParseResult.Failure("The sink URL option can only be specified once.");
                }

                if (!TryReadOptionValue(args, ref index, out sinkUrl))
                {
                    return CliParseResult.Failure("The --sink-url option requires an HTTP or HTTPS URL.");
                }

                sinkUrlSpecified = true;
                continue;
            }

            if (argument.StartsWith($"{SinkUrlOption}=", StringComparison.Ordinal))
            {
                if (sinkUrlSpecified)
                {
                    return CliParseResult.Failure("The sink URL option can only be specified once.");
                }

                sinkUrl = argument[(SinkUrlOption.Length + 1)..];
                if (string.IsNullOrWhiteSpace(sinkUrl))
                {
                    return CliParseResult.Failure("The --sink-url option requires an HTTP or HTTPS URL.");
                }

                sinkUrlSpecified = true;
                continue;
            }

            if (string.Equals(argument, SinkTimeoutOption, StringComparison.Ordinal))
            {
                if (sinkTimeoutSpecified ||
                    !TryReadOptionValue(args, ref index, out var value) ||
                    !TryParseRange(value, 1, 300, out sinkTimeoutSeconds))
                {
                    return CliParseResult.Failure(
                        "The --sink-timeout-seconds option requires an integer between 1 and 300.");
                }

                sinkTimeoutSpecified = true;
                continue;
            }

            if (argument.StartsWith($"{SinkTimeoutOption}=", StringComparison.Ordinal))
            {
                var value = argument[(SinkTimeoutOption.Length + 1)..];
                if (sinkTimeoutSpecified ||
                    !TryParseRange(value, 1, 300, out sinkTimeoutSeconds))
                {
                    return CliParseResult.Failure(
                        "The --sink-timeout-seconds option requires an integer between 1 and 300.");
                }

                sinkTimeoutSpecified = true;
                continue;
            }

            if (string.Equals(argument, SinkFailureModeOption, StringComparison.Ordinal))
            {
                if (sinkFailureModeSpecified ||
                    !TryReadOptionValue(args, ref index, out var value) ||
                    !TryParseFailureMode(value, out sinkFailureMode))
                {
                    return CliParseResult.Failure(
                        "The --sink-failure-mode option requires 'non-fatal' or 'fatal'.");
                }

                sinkFailureModeSpecified = true;
                continue;
            }

            if (argument.StartsWith($"{SinkFailureModeOption}=", StringComparison.Ordinal))
            {
                var value = argument[(SinkFailureModeOption.Length + 1)..];
                if (sinkFailureModeSpecified ||
                    !TryParseFailureMode(value, out sinkFailureMode))
                {
                    return CliParseResult.Failure(
                        "The --sink-failure-mode option requires 'non-fatal' or 'fatal'.");
                }

                sinkFailureModeSpecified = true;
                continue;
            }

            if (string.Equals(argument, SinkMaxAttemptsOption, StringComparison.Ordinal))
            {
                if (sinkMaxAttemptsSpecified ||
                    !TryReadOptionValue(args, ref index, out var value) ||
                    !TryParseRange(value, 1, 5, out sinkMaxAttempts))
                {
                    return CliParseResult.Failure(
                        "The --sink-max-attempts option requires an integer between 1 and 5.");
                }

                sinkMaxAttemptsSpecified = true;
                continue;
            }

            if (argument.StartsWith($"{SinkMaxAttemptsOption}=", StringComparison.Ordinal))
            {
                var value = argument[(SinkMaxAttemptsOption.Length + 1)..];
                if (sinkMaxAttemptsSpecified ||
                    !TryParseRange(value, 1, 5, out sinkMaxAttempts))
                {
                    return CliParseResult.Failure(
                        "The --sink-max-attempts option requires an integer between 1 and 5.");
                }

                sinkMaxAttemptsSpecified = true;
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

        bool hasSinkSpecificOptions =
            sinkUrlSpecified ||
            sinkTimeoutSpecified ||
            sinkFailureModeSpecified ||
            sinkMaxAttemptsSpecified;

        if (sink is null && hasSinkSpecificOptions)
        {
            return CliParseResult.Failure("Sink-specific options require '--sink http'.");
        }

        CliPersistenceOptions persistenceOptions = CliPersistenceOptions.Disabled;
        if (sink is not null)
        {
            if (!string.Equals(sink, "http", StringComparison.Ordinal))
            {
                return CliParseResult.Failure("The selected persistence sink is not supported.");
            }

            if (!TryCreateHttpEndpoint(sinkUrl, out Uri? endpoint))
            {
                return CliParseResult.Failure(
                    "The HTTP sink requires a valid absolute HTTP or HTTPS --sink-url without embedded credentials.");
            }

            persistenceOptions = new CliPersistenceOptions(
                sink,
                endpoint,
                sinkTimeoutSeconds,
                sinkFailureMode,
                sinkMaxAttempts);
        }

        return CliParseResult.Success(new CliOptions(
            repositoryPath ?? ".",
            outputPath,
            verbosity,
            configurationPath,
            disableConfigurationFile,
            excludedPaths.ToArray(),
            classificationOverrides,
            persistenceOptions,
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

    private static bool TryParseRange(
        string value,
        int minimum,
        int maximum,
        out int parsed) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out parsed) &&
        parsed >= minimum &&
        parsed <= maximum;

    private static bool TryParseFailureMode(
        string value,
        out PersistenceFailureMode failureMode)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "non-fatal":
                failureMode = PersistenceFailureMode.NonFatal;
                return true;
            case "fatal":
                failureMode = PersistenceFailureMode.Fatal;
                return true;
            default:
                failureMode = default;
                return false;
        }
    }

    private static bool TryCreateHttpEndpoint(
        string? value,
        out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(candidate.UserInfo))
        {
            return false;
        }

        endpoint = candidate;
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
