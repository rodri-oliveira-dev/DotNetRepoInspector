namespace DotNetRepoInspector.Mcp;

internal static class RepositoryPathBoundary
{
    public static RepositoryPathValidationResult ValidateAndNormalize(
        RepositoryRoot repositoryRoot,
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        if (disableConfigurationFile && configurationPath is not null)
        {
            return RepositoryPathValidationResult.Failure(
                "invalid_tool_input",
                "configurationPath cannot be combined with disableConfigurationFile.");
        }

        var normalizedConfigurationPath = configurationPath;
        if (configurationPath is not null)
        {
            var result = ValidateRelativePath(repositoryRoot.FullPath, configurationPath);
            if (!result.Succeeded)
            {
                return result;
            }

            normalizedConfigurationPath = result.NormalizedPath;
        }

        var normalizedExcludedPaths = new List<string>();
        foreach (var path in excludedPaths ?? Array.Empty<string>())
        {
            var result = ValidateRelativePath(repositoryRoot.FullPath, path);
            if (!result.Succeeded)
            {
                return result;
            }

            normalizedExcludedPaths.Add(result.NormalizedPath!);
        }

        var normalizedOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in classificationOverrides ??
                 new Dictionary<string, string>(StringComparer.Ordinal))
        {
            var result = ValidateRelativePath(repositoryRoot.FullPath, pair.Key);
            if (!result.Succeeded)
            {
                return result;
            }

            normalizedOverrides[result.NormalizedPath!] = pair.Value;
        }

        return RepositoryPathValidationResult.Success(
            normalizedConfigurationPath,
            normalizedExcludedPaths,
            normalizedOverrides);
    }

    private static RepositoryPathValidationResult ValidateRelativePath(
        string repositoryRoot,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RepositoryPathValidationResult.Failure(
                "invalid_tool_input",
                "Repository-relative paths must not be empty.");
        }

        if (Path.IsPathRooted(path))
        {
            return RepositoryPathValidationResult.Failure(
                "path_outside_repository_root",
                "Absolute paths are outside the configured repository boundary.");
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
                return RepositoryPathValidationResult.Failure(
                    "path_outside_repository_root",
                    "The supplied path resolves outside the configured repository root.");
            }

            return RepositoryPathValidationResult.PathSuccess(relativePath.Replace('\\', '/'));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return RepositoryPathValidationResult.Failure(
                "invalid_tool_input",
                "A supplied repository-relative path is invalid.");
        }
    }
}

internal sealed record RepositoryPathValidationResult(
    string? ConfigurationPath,
    IReadOnlyCollection<string>? ExcludedPaths,
    IReadOnlyDictionary<string, string>? ClassificationOverrides,
    string? NormalizedPath,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => ErrorCode is null;

    public static RepositoryPathValidationResult Success(
        string? configurationPath,
        IReadOnlyCollection<string> excludedPaths,
        IReadOnlyDictionary<string, string> classificationOverrides) =>
        new(configurationPath, excludedPaths, classificationOverrides, null, null, null);

    public static RepositoryPathValidationResult PathSuccess(string path) =>
        new(null, null, null, path, null, null);

    public static RepositoryPathValidationResult Failure(string code, string message) =>
        new(null, null, null, null, code, message);
}
