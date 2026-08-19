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

        var properties = NormalizeRequestedNames(request.Properties);
        var items = NormalizeRequestedNames(request.Items ?? []);

        if (properties.Length == 0 && items.Length == 0)
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidRequest,
                "At least one MSBuild property or item is required.");
        }

        var invalidName = properties
            .Concat(items)
            .FirstOrDefault(name => !IsValidMsBuildName(name));
        if (invalidName is not null)
        {
            return Failure(
                MsBuildEvaluationErrorCode.InvalidRequest,
                $"MSBuild property or item name '{invalidName}' is invalid.");
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

        var arguments = new List<string>
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-verbosity:quiet"
        };

        if (properties.Length > 0)
        {
            arguments.Add($"-getProperty:{string.Join(',', properties)}");
        }

        if (items.Length > 0)
        {
            arguments.Add($"-getItem:{string.Join(',', items)}");
        }

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
            if (items.Length == 0 && properties.Length == 1)
            {
                var scalarProperties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [properties[0]] = evaluationResult.StandardOutput.TrimEnd('\r', '\n')
                };

                return MsBuildEvaluationResult.Success(resolvedSdkVersion, scalarProperties);
            }

            using var document = JsonDocument.Parse(evaluationResult.StandardOutput);
            var evaluatedProperties = ParseProperties(document.RootElement, properties);
            var evaluatedItems = ParseItems(document.RootElement, items);

            return MsBuildEvaluationResult.Success(
                resolvedSdkVersion,
                evaluatedProperties,
                evaluatedItems);
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

    private static string[] NormalizeRequestedNames(IEnumerable<string> names) =>
        names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, string> ParseProperties(
        JsonElement root,
        string[] properties)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (properties.Length == 0)
        {
            return result;
        }

        if (!root.TryGetProperty("Properties", out var propertiesElement) ||
            propertiesElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("MSBuild output does not contain a 'Properties' object.");
        }

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

    private static Dictionary<string, IReadOnlyList<MsBuildEvaluationItem>> ParseItems(
        JsonElement root,
        string[] itemNames)
    {
        var result = new Dictionary<string, IReadOnlyList<MsBuildEvaluationItem>>(StringComparer.Ordinal);
        if (itemNames.Length == 0)
        {
            return result;
        }

        if (!root.TryGetProperty("Items", out var itemsElement) ||
            itemsElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("MSBuild output does not contain an 'Items' object.");
        }

        foreach (var itemName in itemNames)
        {
            if (!itemsElement.TryGetProperty(itemName, out var itemArray))
            {
                result[itemName] = [];
                continue;
            }

            if (itemArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"MSBuild item '{itemName}' is not an array.");
            }

            var evaluatedItems = new List<MsBuildEvaluationItem>();
            foreach (var itemElement in itemArray.EnumerateArray())
            {
                if (itemElement.ValueKind != JsonValueKind.Object ||
                    !itemElement.TryGetProperty("Identity", out var identityElement) ||
                    identityElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException($"MSBuild item '{itemName}' does not contain a valid Identity.");
                }

                var identity = identityElement.GetString() ?? string.Empty;
                var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in itemElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "Identity", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();
                }

                evaluatedItems.Add(new MsBuildEvaluationItem(identity, metadata));
            }

            result[itemName] = evaluatedItems;
        }

        return result;
    }

    private static bool IsValidMsBuildName(string name)
    {
        if (name.Length == 0 ||
            (!char.IsLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        return name
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
