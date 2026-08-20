using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace DotNetRepoInspector.MSBuild.Sdk;

public sealed class DotNetSdkInspector : IDotNetSdkInspector
{
    private const int MaxDiagnosticLength = 4_000;
    private readonly string _dotNetExecutable;

    public DotNetSdkInspector(string dotNetExecutable = "dotnet")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotNetExecutable);
        _dotNetExecutable = dotNetExecutable;
    }

    public async Task<DotNetSdkInspectionResult> InspectAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return Failure(
                string.Empty,
                DotNetSdkInspectionErrorCode.InvalidRequest,
                "A repository root is required.");
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                repositoryRoot,
                DotNetSdkInspectionErrorCode.InvalidRequest,
                "The repository root path is invalid.",
                details: exception.Message);
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return Failure(
                normalizedRoot,
                DotNetSdkInspectionErrorCode.RepositoryRootNotFound,
                $"Repository root '{normalizedRoot}' was not found.");
        }

        var globalJsonPath = FindApplicableGlobalJson(normalizedRoot);
        DotNetSdkConfiguration? configuration = null;

        if (globalJsonPath is not null)
        {
            try
            {
                configuration = await ReadConfigurationAsync(globalJsonPath, cancellationToken);
            }
            catch (JsonException exception)
            {
                return Failure(
                    normalizedRoot,
                    DotNetSdkInspectionErrorCode.GlobalJsonInvalid,
                    "The applicable global.json contains invalid JSON.",
                    globalJsonPath,
                    details: exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return Failure(
                    normalizedRoot,
                    DotNetSdkInspectionErrorCode.GlobalJsonInvalid,
                    exception.Message,
                    globalJsonPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return Failure(
                    normalizedRoot,
                    DotNetSdkInspectionErrorCode.GlobalJsonReadFailed,
                    "The applicable global.json could not be read.",
                    globalJsonPath,
                    details: exception.Message);
            }
        }

        ProcessExecutionResult sdkResult;
        try
        {
            sdkResult = await RunDotNetVersionAsync(normalizedRoot, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            return Failure(
                normalizedRoot,
                DotNetSdkInspectionErrorCode.DotNetHostNotFound,
                $"The .NET host '{_dotNetExecutable}' could not be started.",
                globalJsonPath,
                configuration,
                details: exception.Message);
        }

        if (sdkResult.ExitCode != 0)
        {
            return Failure(
                normalizedRoot,
                DotNetSdkInspectionErrorCode.SdkResolutionFailed,
                "The .NET SDK applicable to the inspected repository could not be resolved.",
                globalJsonPath,
                configuration,
                sdkResult.ExitCode,
                DiagnosticDetails(sdkResult));
        }

        var resolvedSdkVersion = sdkResult.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(resolvedSdkVersion))
        {
            return Failure(
                normalizedRoot,
                DotNetSdkInspectionErrorCode.SdkResolutionFailed,
                "The .NET SDK resolver returned an empty version.",
                globalJsonPath,
                configuration);
        }

        return DotNetSdkInspectionResult.Success(
            normalizedRoot,
            globalJsonPath,
            configuration,
            resolvedSdkVersion);
    }

    private static string? FindApplicableGlobalJson(string repositoryRoot)
    {
        DirectoryInfo? directory = new(repositoryRoot);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static async Task<DotNetSdkConfiguration?> ReadConfigurationAsync(
        string globalJsonPath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(globalJsonPath, cancellationToken);
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        if (!document.RootElement.TryGetProperty("sdk", out var sdkElement))
        {
            return null;
        }

        if (sdkElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The 'sdk' property in global.json must be an object.");
        }

        return new DotNetSdkConfiguration(
            ReadOptionalString(sdkElement, "version"),
            ReadOptionalString(sdkElement, "rollForward"),
            ReadOptionalBoolean(sdkElement, "allowPrerelease"));
    }

    private async Task<ProcessExecutionResult> RunDotNetVersionAsync(
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

        SecureProcessEnvironment.HardenDotNetProcess(startInfo);
        startInfo.ArgumentList.Add("--version");

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

    private static string? ReadOptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"The '{propertyName}' property in global.json must be a string.");
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text)
            ? null
            : text.Trim();
    }

    private static bool? ReadOptionalBoolean(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException(
                $"The '{propertyName}' property in global.json must be a boolean.")
        };
    }

    private static DotNetSdkInspectionResult Failure(
        string repositoryRoot,
        DotNetSdkInspectionErrorCode code,
        string message,
        string? globalJsonPath = null,
        DotNetSdkConfiguration? configuration = null,
        int? exitCode = null,
        string? details = null) =>
        DotNetSdkInspectionResult.Failure(
            repositoryRoot,
            new DotNetSdkInspectionError(
                code,
                message,
                exitCode,
                Truncate(details)),
            globalJsonPath,
            configuration);

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
