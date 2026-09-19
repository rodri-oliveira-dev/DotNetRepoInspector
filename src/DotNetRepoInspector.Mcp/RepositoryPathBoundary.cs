namespace DotNetRepoInspector.Mcp;

internal static class RepositoryPathBoundary
{
    public static RepositoryPathValidationResult ValidateRelativeProjectPath(
        RepositoryRoot repositoryRoot,
        string? projectPath)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        return ValidateRelativePath(repositoryRoot.FullPath, projectPath, enforceFileSizeLimit: false);
    }

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

        if (excludedPaths?.Count > McpSecurityLimits.MaxExcludedPaths ||
            classificationOverrides?.Count > McpSecurityLimits.MaxClassificationOverrides)
        {
            return RepositoryPathValidationResult.Failure(
                "input_too_large",
                "The tool request exceeds an input collection limit.");
        }

        var normalizedConfigurationPath = configurationPath;
        if (configurationPath is not null)
        {
            var result = ValidateRelativePath(
                repositoryRoot.FullPath,
                configurationPath,
                enforceFileSizeLimit: true);
            if (!result.Succeeded)
            {
                return result;
            }

            normalizedConfigurationPath = result.NormalizedPath;
        }
        else if (!disableConfigurationFile)
        {
            var result = ValidateRelativePath(
                repositoryRoot.FullPath,
                ".dotnetrepoinspector.json",
                enforceFileSizeLimit: true);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        var normalizedExcludedPaths = new List<string>();
        foreach (var path in excludedPaths ?? Array.Empty<string>())
        {
            var result = ValidateRelativePath(
                repositoryRoot.FullPath,
                path,
                enforceFileSizeLimit: false);
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
            if (string.IsNullOrWhiteSpace(pair.Value) ||
                pair.Value.Length > McpSecurityLimits.MaxClassificationValueLength)
            {
                return RepositoryPathValidationResult.Failure(
                    "invalid_tool_input",
                    "Classification override values must be non-empty and within the supported length limit.");
            }

            var result = ValidateRelativePath(
                repositoryRoot.FullPath,
                pair.Key,
                enforceFileSizeLimit: false);
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
        string? path,
        bool enforceFileSizeLimit)
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

        if (path.Length > McpSecurityLimits.MaxRelativePathLength)
        {
            return RepositoryPathValidationResult.Failure(
                "input_too_large",
                "A repository-relative path exceeds the supported length limit.");
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

            var linkResult = ValidateNoLinks(repositoryRoot, relativePath);
            if (!linkResult.Succeeded)
            {
                return linkResult;
            }

            if (enforceFileSizeLimit &&
                File.Exists(fullPath) &&
                new FileInfo(fullPath).Length > McpSecurityLimits.MaxConfigurationFileBytes)
            {
                return RepositoryPathValidationResult.Failure(
                    "input_too_large",
                    "The configuration file exceeds the supported size limit.");
            }

            return RepositoryPathValidationResult.PathSuccess(relativePath.Replace('\\', '/'));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return RepositoryPathValidationResult.Failure(
                "invalid_tool_input",
                "A supplied repository-relative path is invalid.");
        }
    }

    private static RepositoryPathValidationResult ValidateNoLinks(
        string repositoryRoot,
        string relativePath)
    {
        var currentPath = repositoryRoot;
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(currentPath);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or DirectoryNotFoundException)
            {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return RepositoryPathValidationResult.Failure(
                    "path_through_link",
                    "Tool paths must not traverse symbolic links or junctions below the repository root.");
            }
        }

        return RepositoryPathValidationResult.PathSuccess(relativePath.Replace('\\', '/'));
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
