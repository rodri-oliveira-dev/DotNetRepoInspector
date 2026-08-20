using System.Globalization;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Git;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Diagnostics;
using DotNetRepoInspector.MSBuild.Discovery;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.References;
using DotNetRepoInspector.MSBuild.Sdk;

namespace DotNetRepoInspector.Engine;

public sealed class RepositoryInspector : IRepositoryInspector
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IProjectDiscoverer _projectDiscoverer;
    private readonly IMsBuildProjectFactsEvaluator _projectFactsEvaluator;
    private readonly IDotNetSdkInspector _sdkInspector;
    private readonly IGitRepositoryMetadataProvider _gitMetadataProvider;
    private readonly MsBuildProjectClassificationAdapter _classificationAdapter;

    public RepositoryInspector()
        : this(
            new FileSystemProjectDiscoverer(),
            new MsBuildProjectFactsEvaluator(),
            new DotNetSdkInspector(),
            new GitRepositoryMetadataProvider())
    {
    }

    public RepositoryInspector(
        IProjectDiscoverer projectDiscoverer,
        IMsBuildProjectFactsEvaluator projectFactsEvaluator,
        IDotNetSdkInspector sdkInspector,
        IGitRepositoryMetadataProvider gitMetadataProvider)
    {
        ArgumentNullException.ThrowIfNull(projectDiscoverer);
        ArgumentNullException.ThrowIfNull(projectFactsEvaluator);
        ArgumentNullException.ThrowIfNull(sdkInspector);
        ArgumentNullException.ThrowIfNull(gitMetadataProvider);

        _projectDiscoverer = projectDiscoverer;
        _projectFactsEvaluator = projectFactsEvaluator;
        _sdkInspector = sdkInspector;
        _gitMetadataProvider = gitMetadataProvider;
        _classificationAdapter = new MsBuildProjectClassificationAdapter();
    }

    public Task<InspectionReport> InspectAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default) =>
        InspectAsync(new RepositoryInspectionRequest(repositoryRoot), cancellationToken);

    public async Task<InspectionReport> InspectAsync(
        RepositoryInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var repositoryRoot = NormalizeRepositoryRoot(request.RepositoryRoot);
        var configuration = await InspectionConfigurationResolver.ResolveAsync(
            repositoryRoot,
            request,
            cancellationToken);
        if (!configuration.Succeeded)
        {
            return CreateConfigurationFailureReport(configuration.Error!);
        }

        var diagnostics = new List<InspectionDiagnostic>();

        var gitResult = await _gitMetadataProvider.InspectAsync(
            repositoryRoot,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AddGitDiagnostics(gitResult, diagnostics);

        var sdkResult = await _sdkInspector.InspectAsync(
            repositoryRoot,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AddSdkDiagnostic(repositoryRoot, sdkResult, diagnostics);

        var discoveredProjects = _projectDiscoverer.Discover(
                new ProjectDiscoveryRequest(repositoryRoot, configuration.ExcludedPaths),
                cancellationToken)
            .Where(project => !IsProjectExcluded(project.RelativePath, configuration.ExcludedPaths))
            .ToArray();

        AddUnmatchedClassificationOverrideDiagnostics(
            configuration.ClassificationOverrides,
            discoveredProjects,
            diagnostics);

        var evaluations = new List<EvaluatedProject>(discoveredProjects.Length);
        var successfulProjects = new Dictionary<string, MsBuildProjectFacts>(StringComparer.Ordinal);

        foreach (var discoveredProject in discoveredProjects.OrderBy(
                     static project => project.RelativePath,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizePath(discoveredProject.RelativePath);
            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
            var evaluation = await _projectFactsEvaluator.EvaluateAsync(
                fullPath,
                cancellationToken);

            evaluations.Add(new EvaluatedProject(relativePath, fullPath, evaluation));
            if (evaluation.Succeeded && evaluation.Facts is not null)
            {
                successfulProjects[fullPath] = evaluation.Facts;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var referenceGraph = ProjectReferenceGraphBuilder.Build(
            repositoryRoot,
            successfulProjects);
        var graphByProjectPath = referenceGraph.Projects.ToDictionary(
            static project => project.ProjectPath,
            StringComparer.Ordinal);

        var projects = evaluations
            .Select(evaluation => BuildProjectInspection(
                evaluation,
                graphByProjectPath,
                configuration.ClassificationOverrides))
            .OrderBy(static project => project.Path, StringComparer.Ordinal)
            .ToArray();

        return InspectionReport.Create(
            gitResult.Metadata,
            ToSdkMetadata(repositoryRoot, sdkResult),
            projects,
            OrderDiagnostics(diagnostics));
    }

    private ProjectInspection BuildProjectInspection(
        EvaluatedProject evaluatedProject,
        Dictionary<string, ProjectReferenceGraphNode> graphByProjectPath,
        IReadOnlyDictionary<string, EffectiveClassificationOverride> classificationOverrides)
    {
        var result = evaluatedProject.Result;
        if (!result.Succeeded || result.Facts is null)
        {
            var diagnostic = result.Error is null
                ? InspectionDiagnostics.MsBuildEvaluationFailed(
                    evaluatedProject.RelativePath,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["component"] = "inspection-engine"
                    })
                : MsBuildDiagnosticMapper.FromEvaluationError(
                    result.Error,
                    evaluatedProject.RelativePath);

            return new ProjectInspection(
                evaluatedProject.RelativePath,
                Path.GetFileNameWithoutExtension(evaluatedProject.RelativePath),
                null,
                Array.Empty<ProjectSdkMetadata>(),
                Array.Empty<string>(),
                null,
                null,
                null,
                Array.Empty<string>(),
                null,
                Array.Empty<ProjectReferenceMetadata>(),
                [diagnostic]);
        }

        var facts = result.Facts;
        graphByProjectPath.TryGetValue(
            evaluatedProject.RelativePath,
            out var graphNode);

        var sdks = facts.DeclaredProjectSdks
            .Where(static sdk => !string.IsNullOrWhiteSpace(sdk.Name))
            .OrderBy(static sdk => sdk.Name, StringComparer.Ordinal)
            .ThenBy(static sdk => sdk.Version, StringComparer.Ordinal)
            .Select(static sdk => new ProjectSdkMetadata(sdk.Name, sdk.Version))
            .ToArray();

        var references = graphNode?.References
            .OrderBy(static reference => reference.Path, StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<ProjectReferenceMetadata>();
        var projectDiagnostics = graphNode is null
            ? Array.Empty<InspectionDiagnostic>()
            : OrderDiagnostics(graphNode.Diagnostics);
        var automaticClassification = _classificationAdapter.Classify(facts);
        var classification = ApplyClassificationOverride(
            evaluatedProject.RelativePath,
            automaticClassification,
            classificationOverrides);

        return new ProjectInspection(
            evaluatedProject.RelativePath,
            Path.GetFileNameWithoutExtension(evaluatedProject.RelativePath),
            facts.ResolvedSdkVersion,
            sdks,
            NormalizeValues(facts.TargetFrameworks),
            NormalizeOptionalValue(facts.OutputType),
            facts.IsTestProject,
            facts.IsPackable,
            NormalizeValues(facts.RuntimeIdentifiers),
            classification,
            references,
            projectDiagnostics);
    }

    private static ProjectClassification ApplyClassificationOverride(
        string projectPath,
        ProjectClassification automaticClassification,
        IReadOnlyDictionary<string, EffectiveClassificationOverride> classificationOverrides)
    {
        if (!classificationOverrides.TryGetValue(projectPath, out var configuredOverride))
        {
            return automaticClassification;
        }

        return new ProjectClassification(
            configuredOverride.Kind,
            null,
            automaticClassification.Signals,
            configuredOverride.Source,
            automaticClassification.Kind);
    }

    private static bool IsProjectExcluded(
        string projectPath,
        IReadOnlyList<string> excludedPaths)
    {
        var normalizedProjectPath = NormalizePath(projectPath);
        return excludedPaths.Contains(normalizedProjectPath, PathComparer);
    }

    private static void AddUnmatchedClassificationOverrideDiagnostics(
        IReadOnlyDictionary<string, EffectiveClassificationOverride> classificationOverrides,
        IReadOnlyList<DiscoveredProject> discoveredProjects,
        List<InspectionDiagnostic> diagnostics)
    {
        if (classificationOverrides.Count == 0)
        {
            return;
        }

        var discoveredPaths = discoveredProjects
            .Select(static project => NormalizePath(project.RelativePath))
            .ToHashSet(PathComparer);

        foreach (var pair in classificationOverrides.OrderBy(
                     static pair => pair.Key,
                     StringComparer.Ordinal))
        {
            if (discoveredPaths.Contains(pair.Key))
            {
                continue;
            }

            diagnostics.Add(InspectionDiagnostics.ClassificationOverrideTargetNotFound(
                pair.Key,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["component"] = "configuration",
                    ["overrideSource"] = pair.Value.Source
                }));
        }
    }

    private static InspectionReport CreateConfigurationFailureReport(InspectionDiagnostic diagnostic) =>
        InspectionReport.Create(
            new RepositoryMetadata(null, null, null, null, null),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            [diagnostic]);

    private static string NormalizeRepositoryRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repositoryRoot));
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException(
                $"Repository root '{normalizedRoot}' does not exist.");
        }

        return normalizedRoot;
    }

    private static DotNetSdkMetadata ToSdkMetadata(
        string repositoryRoot,
        DotNetSdkInspectionResult result)
    {
        var globalJsonPath = result.GlobalJsonPath is null
            ? null
            : NormalizePath(Path.GetRelativePath(
                repositoryRoot,
                Path.GetFullPath(result.GlobalJsonPath)));
        var configured = result.Configuration is null
            ? null
            : new ConfiguredDotNetSdk(
                result.Configuration.Version,
                result.Configuration.RollForward,
                result.Configuration.AllowPrerelease);

        return new DotNetSdkMetadata(
            globalJsonPath,
            configured,
            result.ResolvedSdkVersion);
    }

    private static void AddSdkDiagnostic(
        string repositoryRoot,
        DotNetSdkInspectionResult sdkResult,
        List<InspectionDiagnostic> diagnostics)
    {
        if (sdkResult.Succeeded || sdkResult.Error is null)
        {
            return;
        }

        var source = sdkResult.GlobalJsonPath is null
            ? null
            : NormalizePath(Path.GetRelativePath(
                repositoryRoot,
                Path.GetFullPath(sdkResult.GlobalJsonPath)));
        diagnostics.Add(MsBuildDiagnosticMapper.FromSdkInspectionError(
            sdkResult.Error,
            source));
    }

    private static void AddGitDiagnostics(
        GitRepositoryMetadataResult gitResult,
        List<InspectionDiagnostic> diagnostics)
    {
        if (gitResult.Warnings.Count == 0)
        {
            return;
        }

        diagnostics.Add(InspectionDiagnostics.RepositoryMetadataUnavailable(
            "repository",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["component"] = "git",
                ["warningCount"] = gitResult.Warnings.Count.ToString(CultureInfo.InvariantCulture)
            }));
    }

    private static string[] NormalizeValues(IReadOnlyList<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static InspectionDiagnostic[] OrderDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics) =>
        diagnostics
            .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Severity, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Source, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private sealed record EvaluatedProject(
        string RelativePath,
        string FullPath,
        MsBuildProjectFactsResult Result);
}
