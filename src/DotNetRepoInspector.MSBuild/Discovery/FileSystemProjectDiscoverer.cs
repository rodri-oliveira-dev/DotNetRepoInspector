namespace DotNetRepoInspector.MSBuild.Discovery;

public sealed class FileSystemProjectDiscoverer : IProjectDiscoverer
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly HashSet<string> _supportedProjectExtensions;
    private readonly HashSet<string> _excludedDirectoryNames;

    public FileSystemProjectDiscoverer(ProjectDiscoveryOptions? options = null)
    {
        options ??= new ProjectDiscoveryOptions();
        _supportedProjectExtensions = CreateProjectExtensionSet(options.SupportedProjectExtensions);
        _excludedDirectoryNames = CreateExcludedDirectoryNameSet(options.ExcludedDirectoryNames);
    }

    public IReadOnlyList<DiscoveredProject> Discover(ProjectDiscoveryRequest request) =>
        Discover(request, CancellationToken.None);

    public IReadOnlyList<DiscoveredProject> Discover(
        ProjectDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.RepositoryRoot))
        {
            throw new ArgumentException("Repository root must be provided.", nameof(request));
        }

        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.RepositoryRoot));
        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"Repository root '{repositoryRoot}' does not exist.");
        }

        var explicitlyExcludedDirectories = CreateExplicitExclusionSet(
            repositoryRoot,
            request.ExcludedDirectories);

        var pendingDirectories = new Stack<string>();
        var discoveredPaths = new HashSet<string>(StringComparer.Ordinal);
        pendingDirectories.Push(repositoryRoot);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();

            foreach (var filePath in Directory.EnumerateFiles(currentDirectory, "*", EnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_supportedProjectExtensions.Contains(Path.GetExtension(filePath)))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(repositoryRoot, filePath);
                discoveredPaths.Add(NormalizeRelativePath(relativePath));
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(currentDirectory, "*", EnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldExcludeDirectory(directoryPath, explicitlyExcludedDirectories))
                {
                    continue;
                }

                pendingDirectories.Push(directoryPath);
            }
        }

        return discoveredPaths
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => new DiscoveredProject(path))
            .ToArray();
    }

    private bool ShouldExcludeDirectory(
        string directoryPath,
        HashSet<string> explicitlyExcludedDirectories)
    {
        if (_excludedDirectoryNames.Contains(Path.GetFileName(directoryPath)))
        {
            return true;
        }

        var normalizedDirectoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        return explicitlyExcludedDirectories.Contains(normalizedDirectoryPath);
    }

    private static HashSet<string> CreateProjectExtensionSet(IEnumerable<string> projectExtensions)
    {
        ArgumentNullException.ThrowIfNull(projectExtensions);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in projectExtensions)
        {
            if (string.IsNullOrWhiteSpace(extension)
                || !extension.StartsWith('.')
                || extension.Contains(Path.DirectorySeparatorChar)
                || extension.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException(
                    $"Project extension '{extension}' must be a file extension such as '.csproj'.",
                    nameof(projectExtensions));
            }

            result.Add(extension);
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("At least one supported project extension is required.", nameof(projectExtensions));
        }

        return result;
    }

    private static HashSet<string> CreateExcludedDirectoryNameSet(IEnumerable<string> excludedDirectoryNames)
    {
        ArgumentNullException.ThrowIfNull(excludedDirectoryNames);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directoryName in excludedDirectoryNames)
        {
            if (string.IsNullOrWhiteSpace(directoryName)
                || Path.IsPathRooted(directoryName)
                || directoryName.Contains(Path.DirectorySeparatorChar)
                || directoryName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException(
                    $"Excluded directory name '{directoryName}' must be a single directory name.",
                    nameof(excludedDirectoryNames));
            }

            result.Add(directoryName);
        }

        return result;
    }

    private static HashSet<string> CreateExplicitExclusionSet(
        string repositoryRoot,
        IReadOnlyCollection<string>? excludedDirectories)
    {
        var result = new HashSet<string>(PathComparer);
        if (excludedDirectories is null)
        {
            return result;
        }

        foreach (var relativeDirectory in excludedDirectories)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectory) || Path.IsPathRooted(relativeDirectory))
            {
                throw new ArgumentException(
                    "Configured excluded directories must be non-empty paths relative to the repository root.",
                    nameof(excludedDirectories));
            }

            var fullPath = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(repositoryRoot, relativeDirectory)));
            var relativePath = Path.GetRelativePath(repositoryRoot, fullPath);

            if (relativePath == "."
                || Path.IsPathRooted(relativePath)
                || relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Excluded directory '{relativeDirectory}' must stay within the repository root.",
                    nameof(excludedDirectories));
            }

            result.Add(fullPath);
        }

        return result;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return Path.DirectorySeparatorChar == '/'
            ? relativePath
            : relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
