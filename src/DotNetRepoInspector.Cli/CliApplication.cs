using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;
using DotNetRepoInspector.Persistence;

namespace DotNetRepoInspector.Cli;

public sealed class CliApplication
{
    private readonly IRepositoryInspector _repositoryInspector;
    private readonly ICliPersistenceCoordinator _persistenceCoordinator;
    private readonly string _version;

    public CliApplication(IRepositoryInspector repositoryInspector)
        : this(repositoryInspector, new CliPersistenceCoordinator(), GetProductVersion())
    {
    }

    public CliApplication(IRepositoryInspector repositoryInspector, string version)
        : this(repositoryInspector, new CliPersistenceCoordinator(), version)
    {
    }

    public CliApplication(
        IRepositoryInspector repositoryInspector,
        ICliPersistenceCoordinator persistenceCoordinator,
        string version)
    {
        ArgumentNullException.ThrowIfNull(repositoryInspector);
        ArgumentNullException.ThrowIfNull(persistenceCoordinator);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        _repositoryInspector = repositoryInspector;
        _persistenceCoordinator = persistenceCoordinator;
        _version = version;
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
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
            "Command-line arguments were received without logging their values.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["argumentCount"] = args.Count.ToString(CultureInfo.InvariantCulture)
            });

        var parseResult = CliOptionsParser.Parse(args);
        if (!parseResult.Succeeded || parseResult.Options is null)
        {
            console.Logger.Error(
                "cli.invalid-arguments",
                parseResult.Error ?? "Command-line arguments are invalid.");
            return CliExitCodes.InvalidArguments;
        }

        var options = parseResult.Options;
        if (options.ShowHelp)
        {
            console.WriteText(CliHelp.Text);
            return CliExitCodes.Success;
        }

        if (options.ShowVersion)
        {
            console.WriteText(_version);
            return CliExitCodes.Success;
        }

        InspectionReport report;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            console.Logger.Verbose(
                "inspection.start",
                "Repository inspection started.");

            report = await _repositoryInspector.InspectAsync(
                new RepositoryInspectionRequest(
                    options.RepositoryPath,
                    ConfigurationPath: options.ConfigurationPath,
                    DisableConfigurationFile: options.DisableConfigurationFile,
                    ExcludedPaths: options.ExcludedPaths,
                    ClassificationOverrides: options.ClassificationOverrides),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            console.Logger.Warning(
                "inspection.cancelled",
                "Repository inspection was cancelled.");
            return CliExitCodes.Cancelled;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            console.Logger.Error(
                "inspection.failed",
                "Repository inspection failed before a report could be produced.",
                ExceptionContext(exception));
            return CliExitCodes.InspectionFailed;
        }

        string json;
        try
        {
            json = InspectionJsonSerializer.Serialize(report);
        }
        catch (Exception exception) when (
            exception is JsonException or
            InvalidOperationException or
            NotSupportedException)
        {
            console.Logger.Error(
                "inspection.serialization-failed",
                "The inspection report could not be serialized.",
                ExceptionContext(exception));
            return CliExitCodes.InspectionFailed;
        }

        try
        {
            if (options.OutputPath is null)
            {
                console.WriteJson(json);
            }
            else
            {
                await File.WriteAllTextAsync(
                    options.OutputPath,
                    $"{json.TrimEnd()}{Environment.NewLine}",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            console.Logger.Warning(
                "inspection.cancelled",
                "Repository inspection output was cancelled.");
            return CliExitCodes.Cancelled;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            console.Logger.Error(
                "inspection.output-failed",
                "The inspection report could not be written to the requested destination.",
                ExceptionContext(exception));
            return CliExitCodes.OutputFailed;
        }

        InspectionPersistenceResult? persistenceResult;
        try
        {
            persistenceResult = await _persistenceCoordinator.PublishAsync(
                report,
                _version,
                options.Persistence,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            console.Logger.Warning(
                "persistence.cancelled",
                "Snapshot persistence was cancelled.");
            return CliExitCodes.Cancelled;
        }

        if (persistenceResult is not null)
        {
            if (persistenceResult.Succeeded)
            {
                console.Logger.Verbose(
                    "persistence.completed",
                    "Snapshot persistence completed successfully.",
                    PersistenceContext(persistenceResult));
            }
            else if (persistenceResult.ShouldFailExecution)
            {
                console.Logger.Error(
                    "persistence.failed",
                    persistenceResult.Failure?.Message ?? "Snapshot persistence failed.",
                    PersistenceContext(persistenceResult));
                return CliExitCodes.PersistenceFailed;
            }
            else
            {
                console.Logger.Warning(
                    "persistence.failed",
                    persistenceResult.Failure?.Message ?? "Snapshot persistence failed.",
                    PersistenceContext(persistenceResult));
            }
        }

        var hasErrors = HasErrorDiagnostics(report);
        if (hasErrors)
        {
            console.Logger.Warning(
                "inspection.completed-with-errors",
                "Repository inspection completed with error diagnostics.");
            return CliExitCodes.CompletedWithErrors;
        }

        console.Logger.Verbose(
            "inspection.completed",
            "Repository inspection completed successfully.");
        return CliExitCodes.Success;
    }

    private static bool HasErrorDiagnostics(InspectionReport report) =>
        report.Diagnostics.Any(IsError) ||
        report.Projects.Any(project => project.Diagnostics.Any(IsError));

    private static bool IsError(InspectionDiagnostic diagnostic) =>
        string.Equals(
            diagnostic.Severity,
            InspectionDiagnosticSeverity.Error,
            StringComparison.Ordinal);

    private static Dictionary<string, string> ExceptionContext(Exception exception) =>
        new(StringComparer.Ordinal)
        {
            ["exceptionType"] = exception.GetType().Name
        };

    private static Dictionary<string, string> PersistenceContext(
        InspectionPersistenceResult result) =>
        new(StringComparer.Ordinal)
        {
            ["sink"] = result.SinkName,
            ["code"] = result.Failure?.Code ?? "none",
            ["transient"] = (result.Failure?.IsTransient ?? false) ? "true" : "false"
        };

    private static string GetProductVersion()
    {
        var informationalVersion = typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparatorIndex = informationalVersion.IndexOf('+');
            return metadataSeparatorIndex >= 0
                ? informationalVersion[..metadataSeparatorIndex]
                : informationalVersion;
        }

        var version = typeof(CliApplication).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
