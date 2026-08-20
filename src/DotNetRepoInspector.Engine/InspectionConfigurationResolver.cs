using System.Text.Json;
using System.Text.Json.Serialization;

using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Engine;

internal sealed record EffectiveClassificationOverride(string Kind, string Source);

internal sealed record EffectiveInspectionConfiguration(
    IReadOnlyList<string> ExcludedPaths,
    IReadOnlyDictionary<string, EffectiveClassificationOverride> ClassificationOverrides,
    InspectionDiagnostic? Error)
{
    public bool Succeeded => Error is null;

    public static EffectiveInspectionConfiguration Success(
        IReadOnlyList<string> excludedPaths,
        IReadOnlyDictionary<string, EffectiveClassificationOverride> classificationOverrides) =>
        new(excludedPaths, classificationOverrides, null);

    public static EffectiveInspectionConfiguration Failure(InspectionDiagnostic error) =>
        new(
            Array.Empty<string>(),
            new Dictionary<string, EffectiveClassificationOverride>(StringComparer.Ordinal),
            error);
}

internal static class InspectionConfigurationResolver
{
    public const string DefaultFileName = ".dotnetrepoinspector.json";

    private const string SupportedConfigurationSchemaVersion = "1";
    private const string ConfigurationFileOverrideSource = "configuration";
    private const string RequestOverrideSource = "request";

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<EffectiveInspectionConfiguration> ResolveAsync(
        string repositoryRoot,
        RepositoryInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.DisableConfigurationFile && request.ConfigurationPath is not null)
        {
            return Failure("configuration", "conflicting-config-options");
        }

        var excludedPaths = new HashSet<string>(PathComparer);
        var classificationOverrides = new Dictionary<string, EffectiveClassificationOverride>(PathComparer);

        if (!request.DisableConfigurationFile)
        {
            var explicitConfiguration = request.ConfigurationPath is not null;
            string? configurationPath = null;
            string? configurationSource = null;

            if (explicitConfiguration)
            {
                if (!TryNormalizeRelativePath(
                        repositoryRoot,
                        request.ConfigurationPath!,
                        out var normalizedConfigurationPath))
                {
                    return Failure("configuration", "invalid-config-path");
                }

                configurationSource = normalizedConfigurationPath;
                configurationPath = ToFullPath(repositoryRoot, normalizedConfigurationPath);
                if (!File.Exists(configurationPath))
                {
                    return Failure(configurationSource, "config-file-not-found");
                }
            }
            else
            {
                var defaultPath = Path.Combine(repositoryRoot, DefaultFileName);
                if (File.Exists(defaultPath))
                {
                    configurationPath = defaultPath;
                    configurationSource = DefaultFileName;
                }
            }

            if (configurationPath is not null)
            {
                ConfigurationDocument? document;
                try
                {
                    var json = await File.ReadAllTextAsync(configurationPath, cancellationToken);
                    document = JsonSerializer.Deserialize<ConfigurationDocument>(json, JsonOptions);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    JsonException or
                    NotSupportedException)
                {
                    var reason = exception is JsonException
                        ? "invalid-json"
                        : "config-file-read-failed";
                    return Failure(configurationSource, reason);
                }

                if (document is null ||
                    !string.Equals(
                        document.SchemaVersion,
                        SupportedConfigurationSchemaVersion,
                        StringComparison.Ordinal))
                {
                    return Failure(configurationSource, "unsupported-config-schema");
                }

                var exclusionError = AddExcludedPaths(
                    repositoryRoot,
                    document.Exclude,
                    excludedPaths,
                    configurationSource);
                if (exclusionError is not null)
                {
                    return EffectiveInspectionConfiguration.Failure(exclusionError);
                }

                var overrideError = AddClassificationOverrides(
                    repositoryRoot,
                    document.ClassificationOverrides,
                    classificationOverrides,
                    configurationSource,
                    ConfigurationFileOverrideSource,
                    replaceExisting: false);
                if (overrideError is not null)
                {
                    return EffectiveInspectionConfiguration.Failure(overrideError);
                }
            }
        }

        var requestDirectoryError = AddExcludedPaths(
            repositoryRoot,
            request.ExcludedDirectories,
            excludedPaths,
            "configuration");
        if (requestDirectoryError is not null)
        {
            return EffectiveInspectionConfiguration.Failure(requestDirectoryError);
        }

        var requestPathError = AddExcludedPaths(
            repositoryRoot,
            request.ExcludedPaths,
            excludedPaths,
            "configuration");
        if (requestPathError is not null)
        {
            return EffectiveInspectionConfiguration.Failure(requestPathError);
        }

        var requestOverrideError = AddClassificationOverrides(
            repositoryRoot,
            request.ClassificationOverrides,
            classificationOverrides,
            "configuration",
            RequestOverrideSource,
            replaceExisting: true);
        if (requestOverrideError is not null)
        {
            return EffectiveInspectionConfiguration.Failure(requestOverrideError);
        }

        return EffectiveInspectionConfiguration.Success(
            excludedPaths.OrderBy(static path => path, StringComparer.Ordinal).ToArray(),
            classificationOverrides);
    }

    private static InspectionDiagnostic? AddExcludedPaths(
        string repositoryRoot,
        IEnumerable<string>? paths,
        HashSet<string> destination,
        string? source)
    {
        if (paths is null)
        {
            return null;
        }

        foreach (var path in paths)
        {
            if (!TryNormalizeRelativePath(repositoryRoot, path, out var normalizedPath))
            {
                return CreateError(source, "invalid-excluded-path");
            }

            destination.Add(normalizedPath);
        }

        return null;
    }

    private static InspectionDiagnostic? AddClassificationOverrides(
        string repositoryRoot,
        IEnumerable<KeyValuePair<string, string>>? overrides,
        Dictionary<string, EffectiveClassificationOverride> destination,
        string? diagnosticSource,
        string overrideSource,
        bool replaceExisting)
    {
        if (overrides is null)
        {
            return null;
        }

        foreach (var pair in overrides)
        {
            if (!TryNormalizeRelativePath(repositoryRoot, pair.Key, out var normalizedPath))
            {
                return CreateError(diagnosticSource, "invalid-classification-path");
            }

            if (!TryNormalizeClassificationKind(pair.Value, out var normalizedKind))
            {
                return CreateError(diagnosticSource, "invalid-classification-kind");
            }

            if (!replaceExisting && destination.ContainsKey(normalizedPath))
            {
                return CreateError(diagnosticSource, "duplicate-classification-override");
            }

            destination[normalizedPath] = new EffectiveClassificationOverride(
                normalizedKind,
                overrideSource);
        }

        return null;
    }

    private static bool TryNormalizeClassificationKind(string? value, out string normalizedKind)
    {
        normalizedKind = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalizedKind is
            ProjectClassificationKinds.Web or
            ProjectClassificationKinds.Worker or
            ProjectClassificationKinds.Console or
            ProjectClassificationKinds.Library or
            ProjectClassificationKinds.Test or
            ProjectClassificationKinds.Unknown;
    }

    private static bool TryNormalizeRelativePath(
        string repositoryRoot,
        string? path,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            var portablePath = path
                .Trim()
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, portablePath));
            var relativePath = Path.GetRelativePath(repositoryRoot, fullPath);

            if (relativePath == "." ||
                Path.IsPathRooted(relativePath) ||
                relativePath == ".." ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return false;
            }

            normalizedPath = NormalizePath(relativePath);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    private static string ToFullPath(string repositoryRoot, string normalizedRelativePath) =>
        Path.GetFullPath(Path.Combine(
            repositoryRoot,
            normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static EffectiveInspectionConfiguration Failure(string? source, string reason) =>
        EffectiveInspectionConfiguration.Failure(CreateError(source, reason));

    private static InspectionDiagnostic CreateError(string? source, string reason) =>
        InspectionDiagnostics.InvalidConfiguration(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["component"] = "configuration",
                ["reason"] = reason
            });

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private sealed class ConfigurationDocument
    {
        public string? SchemaVersion
        {
            get;
            init;
        }

        public string[]? Exclude
        {
            get;
            init;
        }

        public Dictionary<string, string>? ClassificationOverrides
        {
            get;
            init;
        }
    }
}
