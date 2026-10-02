using System.Text;

using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed class IntegrationDiscoveryPipeline : IIntegrationDiscoveryPipeline
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "artifacts",
            "bin",
            "obj"
        };

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IIntegrationDetector[] _detectors;

    public IntegrationDiscoveryPipeline(IEnumerable<IIntegrationDetector> detectors)
    {
        ArgumentNullException.ThrowIfNull(detectors);

        _detectors = detectors
            .OrderBy(static detector => detector.Id, StringComparer.Ordinal)
            .ToArray();

        if (_detectors.Any(static detector => !IsSafeDetectorId(detector.Id)) ||
            _detectors.Select(static detector => detector.Id).Distinct(StringComparer.Ordinal).Count() !=
            _detectors.Length)
        {
            throw new ArgumentException(
                "Integration detector ids must be non-empty and unique.",
                nameof(detectors));
        }
    }

    public async Task<IntegrationDiscoveryResult> DiscoverAsync(
        IntegrationDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryRoot);
        ArgumentNullException.ThrowIfNull(request.Projects);
        cancellationToken.ThrowIfCancellationRequested();

        var options = request.Options ?? new IntegrationDiscoveryOptions();
        options.Validate();

        string repositoryRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(request.RepositoryRoot));
        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException(
                $"Repository root '{repositoryRoot}' does not exist.");
        }

        var diagnostics = new BoundedDiagnosticCollector(options.MaxDiagnostics);
        var findings = new BoundedFindingCollector(options.MaxFindings);
        var state = new DiscoveryState();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.MaxDuration);
        CancellationToken discoveryToken = timeoutSource.Token;

        try
        {
            SourceCandidate[] sources = DiscoverSources(
                repositoryRoot,
                request.Projects,
                request.ExcludedPaths ?? Array.Empty<string>(),
                options,
                diagnostics,
                state,
                discoveryToken);

            var evidence = new SafeIntegrationEvidence();
            long totalBytes = 0;
            var analyzedFiles = 0;

            foreach (SourceCandidate source in sources)
            {
                discoveryToken.ThrowIfCancellationRequested();

                if (analyzedFiles >= options.MaxSourceFiles)
                {
                    state.Truncated = true;
                    diagnostics.Add(LimitDiagnostic(source.SourcePath, "source-files"));
                    break;
                }

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(source.FullPath);
                    if (!fileInfo.Exists)
                    {
                        diagnostics.Add(FileSkippedDiagnostic(source.SourcePath, "not-found"));
                        continue;
                    }
                }
                catch (Exception exception) when (IsFileAccessException(exception))
                {
                    diagnostics.Add(FileSkippedDiagnostic(source.SourcePath, "metadata-unavailable"));
                    continue;
                }

                if (fileInfo.Length > options.MaxBytesPerFile)
                {
                    state.Truncated = true;
                    diagnostics.Add(FileSkippedDiagnostic(source.SourcePath, "file-size-limit"));
                    continue;
                }

                if (totalBytes + fileInfo.Length > options.MaxTotalBytes)
                {
                    state.Truncated = true;
                    diagnostics.Add(LimitDiagnostic(source.SourcePath, "total-bytes"));
                    break;
                }

                byte[] bytes;
                try
                {
                    bytes = await ReadBoundedFileAsync(
                        source.FullPath,
                        options.MaxBytesPerFile,
                        discoveryToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (IsFileAccessException(exception))
                {
                    diagnostics.Add(FileSkippedDiagnostic(source.SourcePath, "read-failed"));
                    continue;
                }

                if (bytes.Length > options.MaxBytesPerFile)
                {
                    state.Truncated = true;
                    diagnostics.Add(FileSkippedDiagnostic(source.SourcePath, "file-size-limit"));
                    continue;
                }

                if (totalBytes + bytes.LongLength > options.MaxTotalBytes)
                {
                    state.Truncated = true;
                    diagnostics.Add(LimitDiagnostic(source.SourcePath, "total-bytes"));
                    break;
                }

                totalBytes += bytes.LongLength;
                if (HasGeneratedHeader(bytes))
                {
                    continue;
                }

                string sourceText;
                try
                {
                    sourceText = StrictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    diagnostics.Add(FileSkippedDiagnostic(source.SourcePath, "invalid-utf8"));
                    continue;
                }

                analyzedFiles++;
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                    SourceText.From(sourceText, Encoding.UTF8),
                    CSharpParseOptions.Default,
                    source.SourcePath,
                    discoveryToken);
                if (syntaxTree.GetDiagnostics(discoveryToken)
                    .Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    diagnostics.Add(InspectionDiagnostics.IntegrationDiscoveryParseFailed(
                        source.SourcePath,
                        Context("syntax-errors")));
                    continue;
                }

                var root = (CompilationUnitSyntax)await syntaxTree.GetRootAsync(discoveryToken);
                findings.SetSource(
                    source.ProjectPath,
                    source.SourcePath,
                    syntaxTree.GetText(discoveryToken).Lines.Count);
                var context = new IntegrationDetectionContext(
                    source.ProjectPath,
                    source.SourcePath,
                    syntaxTree,
                    root,
                    evidence,
                    findings);

                foreach (IIntegrationDetector detector in _detectors)
                {
                    discoveryToken.ThrowIfCancellationRequested();
                    int rejectedBefore = findings.RejectedCount;

                    try
                    {
                        await detector.DetectAsync(context, discoveryToken);
                    }
                    catch (OperationCanceledException) when (discoveryToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (!IsFatalException(exception))
                    {
                        diagnostics.Add(InspectionDiagnostics.IntegrationDetectorFailed(
                            source.SourcePath,
                            Context("detector-failed", detector.Id)));
                    }

                    if (findings.RejectedCount > rejectedBefore)
                    {
                        diagnostics.Add(InspectionDiagnostics.IntegrationDetectorFailed(
                            source.SourcePath,
                            Context("unsafe-finding-rejected", detector.Id)));
                    }

                    if (findings.LimitReached)
                    {
                        state.Truncated = true;
                        diagnostics.Add(LimitDiagnostic(source.SourcePath, "findings"));
                        break;
                    }
                }

                if (findings.LimitReached)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            state.Truncated = true;
            diagnostics.Add(LimitDiagnostic(null, "duration"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new IntegrationDiscoveryResult(
            findings.ToArray(),
            diagnostics.ToArray(),
            state.Truncated || diagnostics.LimitReached || findings.LimitReached);
    }

    private static SourceCandidate[] DiscoverSources(
        string repositoryRoot,
        IReadOnlyList<IntegrationDiscoveryProject> projects,
        IReadOnlyList<string> excludedPaths,
        IntegrationDiscoveryOptions options,
        BoundedDiagnosticCollector diagnostics,
        DiscoveryState state,
        CancellationToken cancellationToken)
    {
        HashSet<string> exclusions = CreateExclusionSet(repositoryRoot, excludedPaths);
        HashSet<string> excludedProjectDirectories = exclusions
            .Where(IsProjectFile)
            .Select(static path => Path.GetDirectoryName(path)!)
            .ToHashSet(PathComparer);
        ProjectRoot[] projectRoots = projects
            .OrderBy(static project => project.ProjectPath, StringComparer.Ordinal)
            .Select(project => TryCreateProjectRoot(repositoryRoot, project.ProjectPath))
            .Where(static project => project is not null)
            .Select(static project => project!)
            .Where(project => !IsExcluded(project.ProjectFilePath, exclusions))
            .ToArray();
        var allProjectDirectories = projectRoots
            .Select(static project => project.DirectoryPath)
            .ToHashSet(PathComparer);
        var sources = new Dictionary<string, SourceCandidate>(PathComparer);
        var visitedPaths = 0;

        foreach (ProjectRoot project in projectRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(project.DirectoryPath))
            {
                diagnostics.Add(FileSkippedDiagnostic(project.ProjectPath, "project-directory-not-found"));
                continue;
            }

            var pendingDirectories = new Stack<string>();
            var visitedDirectories = new HashSet<string>(PathComparer);
            pendingDirectories.Push(project.DirectoryPath);

            if (++visitedPaths > options.MaxVisitedPaths)
            {
                state.Truncated = true;
                diagnostics.Add(LimitDiagnostic(project.ProjectPath, "visited-paths"));
                return OrderSources(sources);
            }

            while (pendingDirectories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pendingDirectories.Pop();
                if (!visitedDirectories.Add(directory))
                {
                    continue;
                }

                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.cs", EnumerationOptions))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++visitedPaths > options.MaxVisitedPaths)
                        {
                            state.Truncated = true;
                            diagnostics.Add(LimitDiagnostic(ToRelativePath(repositoryRoot, file), "visited-paths"));
                            return OrderSources(sources);
                        }

                        if (IsExcluded(file, exclusions) || IsGeneratedFileName(file))
                        {
                            continue;
                        }

                        string sourcePath = ToRelativePath(repositoryRoot, file);
                        var candidate = new SourceCandidate(project.ProjectPath, sourcePath, file);
                        sources[$"{project.ProjectPath}\0{file}"] = candidate;
                    }
                }
                catch (Exception exception) when (IsFileAccessException(exception))
                {
                    diagnostics.Add(FileSkippedDiagnostic(
                        ToRelativePath(repositoryRoot, directory),
                        "directory-unavailable"));
                    continue;
                }

                var directories = new List<string>();
                try
                {
                    foreach (string child in Directory.EnumerateDirectories(directory, "*", EnumerationOptions))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++visitedPaths > options.MaxVisitedPaths)
                        {
                            state.Truncated = true;
                            diagnostics.Add(LimitDiagnostic(ToRelativePath(repositoryRoot, child), "visited-paths"));
                            return OrderSources(sources);
                        }

                        directories.Add(child);
                    }
                }
                catch (Exception exception) when (IsFileAccessException(exception))
                {
                    diagnostics.Add(FileSkippedDiagnostic(
                        ToRelativePath(repositoryRoot, directory),
                        "directory-unavailable"));
                    continue;
                }

                foreach (string child in directories.OrderByDescending(
                             static path => path,
                             StringComparer.Ordinal))
                {
                    if (IsExcluded(child, exclusions) ||
                        ExcludedDirectoryNames.Contains(Path.GetFileName(child)) ||
                        excludedProjectDirectories.Contains(child) &&
                        !PathComparer.Equals(child, project.DirectoryPath) ||
                        allProjectDirectories.Contains(child) && !PathComparer.Equals(child, project.DirectoryPath))
                    {
                        continue;
                    }

                    pendingDirectories.Push(child);
                }
            }
        }

        return OrderSources(sources);
    }

    private static SourceCandidate[] OrderSources(
        Dictionary<string, SourceCandidate> sources) =>
        sources.Values
            .OrderBy(static source => source.SourcePath, StringComparer.Ordinal)
            .ThenBy(static source => source.ProjectPath, StringComparer.Ordinal)
            .ToArray();

    private static ProjectRoot? TryCreateProjectRoot(
        string repositoryRoot,
        string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || Path.IsPathRooted(projectPath))
        {
            return null;
        }

        string projectFilePath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            projectPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinRoot(repositoryRoot, projectFilePath))
        {
            return null;
        }

        return new ProjectRoot(
            ToRelativePath(repositoryRoot, projectFilePath),
            projectFilePath,
            Path.GetDirectoryName(projectFilePath)!);
    }

    private static HashSet<string> CreateExclusionSet(
        string repositoryRoot,
        IReadOnlyList<string> excludedPaths)
    {
        var result = new HashSet<string>(PathComparer);
        foreach (string path in excludedPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                path.Replace('/', Path.DirectorySeparatorChar)));
            if (IsWithinRoot(repositoryRoot, fullPath))
            {
                result.Add(Path.TrimEndingDirectorySeparator(fullPath));
            }
        }

        return result;
    }

    private static bool IsExcluded(string path, HashSet<string> exclusions)
    {
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach (string exclusion in exclusions)
        {
            if (PathComparer.Equals(normalizedPath, exclusion) ||
                normalizedPath.StartsWith(
                    $"{exclusion}{Path.DirectorySeparatorChar}",
                    PathComparison))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGeneratedFileName(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeDetectorId(string? id) =>
        id is { Length: > 0 and <= 128 } &&
        id.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool HasGeneratedHeader(byte[] bytes)
    {
        int length = Math.Min(bytes.Length, 2048);
        string prefix = Encoding.UTF8.GetString(bytes, 0, length);
        return prefix.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[maximumBytes + 1];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead != buffer.Length)
        {
            Array.Resize(ref buffer, totalRead);
        }

        return buffer;
    }

    private static InspectionDiagnostic FileSkippedDiagnostic(string source, string reason) =>
        InspectionDiagnostics.IntegrationDiscoveryFileSkipped(source, Context(reason));

    private static InspectionDiagnostic LimitDiagnostic(string? source, string limit) =>
        InspectionDiagnostics.IntegrationDiscoveryLimitReached(source, Context(limit));

    private static Dictionary<string, string> Context(
        string reason,
        string? detector = null)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["component"] = "integration-discovery",
            ["reason"] = reason
        };
        if (detector is not null)
        {
            context["detector"] = detector;
        }

        return context;
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
        NormalizePath(Path.GetRelativePath(repositoryRoot, path));

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static bool IsFileAccessException(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        NotSupportedException or
        PathTooLongException;

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException or
        StackOverflowException or
        AccessViolationException;

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

    private sealed record ProjectRoot(
        string ProjectPath,
        string ProjectFilePath,
        string DirectoryPath);

    private sealed record SourceCandidate(
        string ProjectPath,
        string SourcePath,
        string FullPath);

    private sealed class DiscoveryState
    {
        public bool Truncated
        {
            get;
            set;
        }
    }

    private sealed class BoundedDiagnosticCollector(int maximumCount)
    {
        private readonly List<InspectionDiagnostic> _diagnostics = [];

        public bool LimitReached
        {
            get;
            private set;
        }

        public void Add(InspectionDiagnostic diagnostic)
        {
            if (_diagnostics.Count < maximumCount)
            {
                _diagnostics.Add(diagnostic);
                return;
            }

            LimitReached = true;
            if (_diagnostics.Count > 0 &&
                _diagnostics[^1].Code != InspectionDiagnosticCodes.IntegrationDiscoveryLimitReached)
            {
                _diagnostics[^1] = LimitDiagnostic(null, "diagnostics");
            }
        }

        public InspectionDiagnostic[] ToArray() =>
            _diagnostics
                .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Source, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Context?["reason"], StringComparer.Ordinal)
                .ToArray();
    }

    private sealed class BoundedFindingCollector(int maximumCount) : IIntegrationFindingCollector
    {
        private readonly Dictionary<string, IntegrationFinding> _findings =
            new(StringComparer.Ordinal);
        private string _projectPath = string.Empty;
        private string _sourcePath = string.Empty;
        private int _maximumLine;

        public bool LimitReached
        {
            get;
            private set;
        }

        public int RejectedCount
        {
            get;
            private set;
        }

        public void SetSource(string projectPath, string sourcePath, int maximumLine)
        {
            _projectPath = projectPath;
            _sourcePath = sourcePath;
            _maximumLine = maximumLine;
        }

        public bool TryAdd(IntegrationFindingCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            if (!IsSafeCandidate(candidate) ||
                candidate.Line > _maximumLine)
            {
                RejectedCount++;
                return false;
            }

            IntegrationFinding finding = IntegrationFinding.Create(
                _projectPath,
                candidate.Kind,
                candidate.Direction,
                candidate.Technology,
                new IntegrationSourceLocation(_sourcePath, candidate.Line),
                candidate.Confidence,
                candidate.Signals,
                candidate.Target,
                candidate.ResourceType,
                candidate.ConfigurationKey,
                candidate.Contract);

            if (_findings.TryGetValue(finding.Id, out IntegrationFinding? existing))
            {
                _findings[finding.Id] = existing with
                {
                    Confidence = StrongerConfidence(existing.Confidence, finding.Confidence),
                    Signals = existing.Signals
                        .Concat(finding.Signals)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static signal => signal, StringComparer.Ordinal)
                        .ToArray()
                };
                return true;
            }

            if (_findings.Count >= maximumCount)
            {
                LimitReached = true;
                return false;
            }

            _findings.Add(finding.Id, finding);
            return true;
        }

        public IntegrationFinding[] ToArray() =>
            _findings.Values
                .OrderBy(static finding => finding.ProjectPath, StringComparer.Ordinal)
                .ThenBy(static finding => finding.Source.Path, StringComparer.Ordinal)
                .ThenBy(static finding => finding.Source.Line)
                .ThenBy(static finding => finding.Id, StringComparer.Ordinal)
                .ToArray();

        private static bool IsSafeCandidate(IntegrationFindingCandidate candidate) =>
            IntegrationKind.IsDefined(candidate.Kind) &&
            IntegrationDirection.IsDefined(candidate.Direction) &&
            IntegrationConfidence.IsDefined(candidate.Confidence) &&
            candidate.Line > 0 &&
            IsSlug(candidate.Technology) &&
            IsOptionalIdentifier(candidate.Target) &&
            IsOptionalSlug(candidate.ResourceType) &&
            IsOptionalConfigurationKey(candidate.ConfigurationKey) &&
            IsOptionalIdentifier(candidate.Contract) &&
            candidate.Signals is { Count: > 0 } &&
            candidate.Signals.All(IsSignal);

        private static bool IsOptionalIdentifier(string? value) =>
            value is null || IsRestricted(
                value,
                static character =>
                    char.IsLetterOrDigit(character) ||
                    character is '.' or '_' or '-' or '/' or ':' or '+' or '<' or '>' or '[' or ']');

        private static bool IsOptionalSlug(string? value) => value is null || IsSlug(value);

        private static bool IsSlug(string? value) =>
            IsRestricted(
                value,
                static character =>
                    char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

        private static bool IsSignal(string? value) =>
            IsRestricted(
                value,
                static character =>
                    char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '_' or '-');

        private static bool IsOptionalConfigurationKey(string? value) =>
            value is null || IsRestricted(
                value,
                static character =>
                    char.IsLetterOrDigit(character) ||
                    character is ':' or '.' or '_' or '-' or '[' or ']');

        private static bool IsRestricted(string? value, Func<char, bool> predicate) =>
            value is { Length: > 0 and <= 256 } && value.All(predicate);

        private static string StrongerConfidence(string first, string second)
        {
            if (first == IntegrationConfidence.High || second == IntegrationConfidence.High)
            {
                return IntegrationConfidence.High;
            }

            return first == IntegrationConfidence.Medium || second == IntegrationConfidence.Medium
                ? IntegrationConfidence.Medium
                : IntegrationConfidence.Low;
        }
    }
}
