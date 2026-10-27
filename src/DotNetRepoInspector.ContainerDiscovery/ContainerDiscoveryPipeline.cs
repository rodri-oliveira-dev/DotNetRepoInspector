using System.Text;

namespace DotNetRepoInspector.ContainerDiscovery;

public sealed class ContainerDiscoveryPipeline : IContainerDiscoveryPipeline
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "artifacts",
        "bin",
        "obj"
    };

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<ContainerDiscoveryResult> DiscoverAsync(
        ContainerDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryRoot);
        cancellationToken.ThrowIfCancellationRequested();

        ContainerDiscoveryOptions options = request.Options ?? new ContainerDiscoveryOptions();
        options.Validate();

        string repositoryRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(request.RepositoryRoot));
        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException(
                $"Repository root '{repositoryRoot}' does not exist.");
        }

        EnsureRootIsNotReparsePoint(repositoryRoot);
        HashSet<string> exclusions = NormalizeExclusions(
            repositoryRoot,
            request.ExcludedPaths ?? Array.Empty<string>());
        var diagnostics = new BoundedDiagnosticCollector(options.MaxDiagnostics);
        var images = new List<DockerImageEvidence>();
        bool truncated = false;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.MaxDuration);
        CancellationToken discoveryToken = timeoutSource.Token;

        try
        {
            DiscoveryCandidates candidates = DiscoverCandidates(
                repositoryRoot,
                exclusions,
                options,
                diagnostics,
                discoveryToken);
            truncated |= candidates.Truncated;
            long totalBytes = 0;
            int processedFiles = 0;

            foreach (DockerfileCandidate candidate in candidates.Files)
            {
                discoveryToken.ThrowIfCancellationRequested();

                if (processedFiles >= options.MaxDockerfiles)
                {
                    truncated = true;
                    diagnostics.Add(LimitDiagnostic(candidate.SourcePath, "dockerfiles"));
                    break;
                }

                processedFiles++;

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(candidate.FullPath);
                    if (!fileInfo.Exists)
                    {
                        diagnostics.Add(FileSkippedDiagnostic(candidate.SourcePath, "not-found"));
                        continue;
                    }

                    if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add(FileSkippedDiagnostic(candidate.SourcePath, "reparse-point"));
                        continue;
                    }
                }
                catch (Exception exception) when (IsFileAccessException(exception))
                {
                    diagnostics.Add(FileSkippedDiagnostic(candidate.SourcePath, "metadata-unavailable"));
                    continue;
                }

                if (fileInfo.Length > options.MaxBytesPerFile)
                {
                    truncated = true;
                    diagnostics.Add(FileSkippedDiagnostic(candidate.SourcePath, "file-size-limit"));
                    continue;
                }

                if (totalBytes + fileInfo.Length > options.MaxTotalBytes)
                {
                    truncated = true;
                    diagnostics.Add(LimitDiagnostic(candidate.SourcePath, "total-bytes"));
                    break;
                }

                byte[] bytes;
                try
                {
                    bytes = await ReadBoundedFileAsync(
                        candidate.FullPath,
                        options.MaxBytesPerFile,
                        discoveryToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (IsFileAccessException(exception))
                {
                    diagnostics.Add(FileSkippedDiagnostic(candidate.SourcePath, "read-failed"));
                    continue;
                }

                if (bytes.Length > options.MaxBytesPerFile)
                {
                    truncated = true;
                    diagnostics.Add(FileSkippedDiagnostic(candidate.SourcePath, "file-size-limit"));
                    continue;
                }

                if (totalBytes + bytes.LongLength > options.MaxTotalBytes)
                {
                    truncated = true;
                    diagnostics.Add(LimitDiagnostic(candidate.SourcePath, "total-bytes"));
                    break;
                }

                string content;
                try
                {
                    content = StrictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    diagnostics.Add(FileSkippedDiagnostic(candidate.SourcePath, "invalid-utf8"));
                    continue;
                }

                totalBytes += bytes.LongLength;
                DockerfileParser.ParseResult parsed = DockerfileParser.Parse(
                    candidate.SourcePath,
                    content,
                    discoveryToken);
                foreach (ContainerDiscoveryDiagnostic diagnostic in parsed.Diagnostics)
                {
                    diagnostics.Add(diagnostic);
                }

                foreach (DockerImageEvidence image in parsed.Images)
                {
                    if (images.Count >= options.MaxResults)
                    {
                        truncated = true;
                        diagnostics.Add(LimitDiagnostic(candidate.SourcePath, "results"));
                        break;
                    }

                    images.Add(image);
                }

                if (images.Count >= options.MaxResults)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            truncated = true;
            diagnostics.Add(LimitDiagnostic(null, "duration"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ContainerDiscoveryResult(
            images
                .OrderBy(static image => image.SourcePath, StringComparer.Ordinal)
                .ThenBy(static image => image.Line)
                .ThenBy(static image => image.ImageReference, StringComparer.Ordinal)
                .ToArray(),
            diagnostics.ToArray(),
            truncated || diagnostics.LimitReached);
    }

    private static DiscoveryCandidates DiscoverCandidates(
        string repositoryRoot,
        IReadOnlySet<string> exclusions,
        ContainerDiscoveryOptions options,
        BoundedDiagnosticCollector diagnostics,
        CancellationToken cancellationToken)
    {
        var files = new List<DockerfileCandidate>();
        var directories = new Stack<DirectoryCandidate>();
        directories.Push(new DirectoryCandidate(repositoryRoot, 0));
        int visitedPaths = 0;
        bool truncated = false;

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryCandidate current = directories.Pop();

            string[] entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(
                        current.FullPath,
                        "*",
                        EnumerationOptions)
                    .OrderBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception) when (IsFileAccessException(exception))
            {
                diagnostics.Add(FileSkippedDiagnostic(
                    ToRelativePath(repositoryRoot, current.FullPath),
                    "directory-unavailable"));
                continue;
            }

            var childDirectories = new List<DirectoryCandidate>();
            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visitedPaths > options.MaxVisitedPaths)
                {
                    truncated = true;
                    diagnostics.Add(LimitDiagnostic(
                        ToRelativePath(repositoryRoot, entry),
                        "visited-paths"));
                    return new DiscoveryCandidates(files, truncated);
                }

                string relativePath = ToRelativePath(repositoryRoot, entry);
                if (IsExcluded(relativePath, exclusions))
                {
                    continue;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (IsFileAccessException(exception))
                {
                    diagnostics.Add(FileSkippedDiagnostic(relativePath, "metadata-unavailable"));
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (ExcludedDirectoryNames.Contains(Path.GetFileName(entry)))
                    {
                        continue;
                    }

                    if (current.Depth >= options.MaxDepth)
                    {
                        truncated = true;
                        diagnostics.Add(LimitDiagnostic(relativePath, "depth"));
                        continue;
                    }

                    childDirectories.Add(new DirectoryCandidate(entry, current.Depth + 1));
                    continue;
                }

                if (IsDockerfileName(Path.GetFileName(entry)))
                {
                    files.Add(new DockerfileCandidate(relativePath, entry));
                }
            }

            for (int index = childDirectories.Count - 1; index >= 0; index--)
            {
                directories.Push(childDirectories[index]);
            }
        }

        return new DiscoveryCandidates(files, truncated);
    }

    private static bool IsDockerfileName(string fileName) =>
        fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> NormalizeExclusions(
        string repositoryRoot,
        IEnumerable<string> excludedPaths)
    {
        var result = new HashSet<string>(PathComparer);
        foreach (string excludedPath in excludedPaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(excludedPath);
            if (Path.IsPathRooted(excludedPath))
            {
                throw new ArgumentException(
                    "Container discovery exclusions must be repository-relative.",
                    nameof(excludedPaths));
            }

            string fullPath = Path.GetFullPath(excludedPath, repositoryRoot);
            if (!IsWithinRoot(repositoryRoot, fullPath))
            {
                throw new ArgumentException(
                    "Container discovery exclusions must stay within the repository root.",
                    nameof(excludedPaths));
            }

            result.Add(ToRelativePath(repositoryRoot, fullPath).TrimEnd('/'));
        }

        return result;
    }

    private static bool IsExcluded(string relativePath, IReadOnlySet<string> exclusions) =>
        exclusions.Any(exclusion =>
            relativePath.Equals(exclusion, PathComparison) ||
            relativePath.StartsWith($"{exclusion}/", PathComparison));

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int capacity = (int)Math.Min(stream.Length, maximumBytes + 1L);
        using var buffer = new MemoryStream(capacity);
        var chunk = new byte[16_384];

        while (buffer.Length <= maximumBytes)
        {
            int remaining = maximumBytes + 1 - (int)buffer.Length;
            int read = await stream.ReadAsync(
                chunk.AsMemory(0, Math.Min(chunk.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static void EnsureRootIsNotReparsePoint(string repositoryRoot)
    {
        if ((File.GetAttributes(repositoryRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException(
                "Container discovery does not accept a reparse point as the repository root.",
                nameof(repositoryRoot));
        }
    }

    private static bool IsWithinRoot(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." &&
               !Path.IsPathRooted(relative) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string ToRelativePath(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    private static ContainerDiscoveryDiagnostic LimitDiagnostic(string? source, string reason) =>
        new(ContainerDiscoveryDiagnosticCodes.LimitReached, reason, source);

    private static ContainerDiscoveryDiagnostic FileSkippedDiagnostic(string source, string reason) =>
        new(ContainerDiscoveryDiagnosticCodes.FileSkipped, reason, source);

    private static bool IsFileAccessException(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        NotSupportedException or
        PathTooLongException;

    private static StringComparer PathComparer
    {
        get;
    } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison
    {
        get;
    } = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record DirectoryCandidate(string FullPath, int Depth);

    private sealed record DockerfileCandidate(string SourcePath, string FullPath);

    private sealed record DiscoveryCandidates(
        IReadOnlyList<DockerfileCandidate> Files,
        bool Truncated);

    private sealed class BoundedDiagnosticCollector(int maximumCount)
    {
        private readonly List<ContainerDiscoveryDiagnostic> _diagnostics = [];

        public bool LimitReached
        {
            get; private set;
        }

        public void Add(ContainerDiscoveryDiagnostic diagnostic)
        {
            if (_diagnostics.Count < maximumCount)
            {
                _diagnostics.Add(diagnostic);
                return;
            }

            LimitReached = true;
            if (_diagnostics.Count > 0 &&
                _diagnostics[^1].Code != ContainerDiscoveryDiagnosticCodes.LimitReached)
            {
                _diagnostics[^1] = LimitDiagnostic(null, "diagnostics");
            }
        }

        public ContainerDiscoveryDiagnostic[] ToArray() =>
            _diagnostics
                .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Reason, StringComparer.Ordinal)
                .ToArray();
    }
}
