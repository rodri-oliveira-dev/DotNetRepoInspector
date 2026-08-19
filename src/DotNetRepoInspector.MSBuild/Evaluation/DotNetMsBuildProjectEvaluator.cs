using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed class DotNetMsBuildProjectEvaluator : IMsBuildProjectEvaluator
{
    private const int MaxDiagnosticLength = 4_000;
    private static readonly string[] SdkVersionArguments = ["--version"];
    private readonly string _dotNetExecutable;

    public DotNetMsBuildProjectEvaluator(string dotNetExecutable = "dotnet")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotNetExecutable);
        _dotNetExecutable = dotNetExecutable;
    }

    public async Task<MsBuildEvaluationResult> EvaluateAsync(
        MsBuildEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidRequest,
                "A project path is required.");
        }

        string projectPath;
        try
        {
            projectPath = Path.GetFullPath(request.ProjectPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidRequest,
                "The project path is invalid.",
                details: exception.Message);
        }

        if (!File.Exists(projectPath))
        {
            return Failure(
                MsBuildEvaluationErrorCode.ProjectNotFound,
                $"Project '{projectPath}' was not found.");
        }

        var properties = request.Properties
            .Where(property => !string.IsNullOrWhiteSpace(property))
            .Select(property => property.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(property => property, StringComparer.Ordinal)
            .ToArray();

        if (properties.Length == 0)
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidRequest,
                "At least one MSBuild property is required.");
        }

        var invalidProperty = properties.FirstOrDefault(property => !IsValidPropertyName(property));
        if (invalidProperty is not null)
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidRequest,
                $"MSBuild property name '{invalidProperty}' is invalid.");
        }

        var workingDirectory = Path.GetDirectoryName(projectPath)!;

        ProcessExecutionResult sdkResult;
        try
        {
            sdkResult = await RunDotNetAsync(
                SdkVersionArguments,
                workingDirectory,
                cancellationToken);
        }
        catch (Win32Exception exception)
        {
            return Failure(
                MsBuildEvaluationErrorCode.DotNetHostNotFound,
                $"The .NET host '{_dotNetExecutable}' could not be started.",
                details: exception.Message);
        }

        if (sdkResult.ExitCode != 0)
        {
            return Failure(
                MsBuildEvaluationErrorCode.SdkResolutionFailed,
                "The .NET SDK required by the project could not be resolved.",
                sdkResult.ExitCode,
                DiagnosticDetails(sdkResult));
        }

        var resolvedSdkVersion = sdkResult.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(resolvedSdkVersion))
        {
            return Failure(
                MsBuildEvaluationErrorCode.SdkResolutionFailed,
                "The .NET SDK resolver returned an empty version.");
        }

        var arguments = new[]
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-verbosity:quiet",
            $"-getProperty:{string.Join(',', properties)}"
        };

        ProcessExecutionResult evaluationResult;
        try
        {
            evaluationResult = await RunDotNetAsync(
                arguments,
                workingDirectory,
                cancellationToken);
        }
        catch (Win32Exception exception)
        {
            return Failure(
                MsBuildEvaluationErrorCode.DotNetHostNotFound,
                $"The .NET host '{_dotNetExecutable}' could not be started.",
                details: exception.Message);
        }

        if (evaluationResult.ExitCode != 0)
        {
            return Failure(
                MsBuildEvaluationErrorCode.MsBuildEvaluationFailed,
                "MSBuild failed while evaluating the project.",
                evaluationResult.ExitCode,
                DiagnosticDetails(evaluationResult));
        }

        try
        {
            var evaluatedProperties = ParseProperties(evaluationResult.StandardOutput, properties);
            return MsBuildEvaluationResult.Success(resolvedSdkVersion, evaluatedProperties);
        }
        catch (JsonException exception)
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidMsBuildOutput,
                "MSBuild returned output that could not be parsed.",
                details: Truncate(exception.Message));
        }
        catch (InvalidDataException exception)
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidMsBuildOutput,
                exception.Message);
        }
    }

    private async Task<ProcessExecutionResult> RunDotNetAsync(
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _dotNetExecutable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["DOTNET_NOLOGO"] = "true";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new Win32Exception($"Unable to start '{_dotNetExecutable}'.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ProcessExecutionResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static Dictionary<string, string> ParseProperties(
        string standardOutput,
        string[] properties)
    {
        if (properties.Length == 1)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [properties[0]] = standardOutput.TrimEnd('\r', '\n')
            };
        }

        using var document = JsonDocument.Parse(standardOutput);
        if (!document.RootElement.TryGetProperty("Properties", out var propertiesElement) ||
            propertiesElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("MSBuild output does not contain a 'Properties' object.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!propertiesElement.TryGetProperty(property, out var propertyElement))
            {
                throw new InvalidDataException($"MSBuild output does not contain property '{property}'.");
            }

            result[property] = propertyElement.ValueKind == JsonValueKind.String
                ? propertyElement.GetString() ?? string.Empty
                : propertyElement.ToString();
        }

        return result;
    }

    private static bool IsValidPropertyName(string propertyName)
    {
        if (propertyName.Length == 0 ||
            (!char.IsLetter(propertyName[0]) && propertyName[0] != '_'))
        {
            return false;
        }

        return propertyName
            .Skip(1)
            .All(character =>
                char.IsLetterOrDigit(character) ||
                character is '_' or '.' or '-');
    }

    private static MsBuildEvaluationResult Failure(
        MsBuildEvaluationErrorCode code,
        string message,
        int? exitCode = null,
        string? details = null) =>
        MsBuildEvaluationResult.Failure(
            new MsBuildEvaluationError(code, message, exitCode, Truncate(details)));

    private static string? DiagnosticDetails(ProcessExecutionResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return Truncate(details);
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxDiagnosticLength
            ? trimmed
            : trimmed[..MaxDiagnosticLength];
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
    }

    private sealed record ProcessExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
