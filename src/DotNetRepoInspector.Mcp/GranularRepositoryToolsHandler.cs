using DotNetRepoInspector.Core.Contracts;

using ModelContextProtocol.Protocol;

namespace DotNetRepoInspector.Mcp;

public sealed class GranularRepositoryToolsHandler
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly RepositoryInspectionExecutor _executor;

    public GranularRepositoryToolsHandler(RepositoryInspectionExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    public async Task<CallToolResult> ListProjectsAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var outcome = await InspectAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
        if (!outcome.Succeeded)
        {
            return Error(outcome);
        }

        var report = outcome.Report!;
        var projects = report.Projects
            .OrderBy(static project => project.Path, StringComparer.Ordinal)
            .Select(static project => new ProjectSummary(
                project.Path,
                project.Name,
                project.TargetFrameworks,
                project.Classification,
                project.Diagnostics.Count,
                project.Diagnostics.Count(static diagnostic =>
                    diagnostic.Severity == InspectionDiagnosticSeverity.Error),
                project.Diagnostics.Count(static diagnostic =>
                    diagnostic.Severity == InspectionDiagnosticSeverity.Warning)))
            .ToArray();

        return Success(new ListProjectsData(report.SchemaVersion, projects));
    }

    public async Task<CallToolResult> GetProjectDetailsAsync(
        string projectPath,
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var pathValidation = RepositoryPathBoundary.ValidateRelativeProjectPath(
            _executor.RepositoryRoot,
            projectPath);
        if (!pathValidation.Succeeded)
        {
            return McpToolResults.Error(
                pathValidation.ErrorCode!,
                pathValidation.ErrorMessage!);
        }

        var outcome = await InspectAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
        if (!outcome.Succeeded)
        {
            return Error(outcome);
        }

        var report = outcome.Report!;
        var project = report.Projects.SingleOrDefault(candidate =>
            string.Equals(
                candidate.Path,
                pathValidation.NormalizedPath,
                PathComparison));
        if (project is null)
        {
            return McpToolResults.Error(
                "project_not_found",
                "The requested project was not found in the inspection result.");
        }

        return Success(new GetProjectDetailsData(report.SchemaVersion, project));
    }

    public async Task<CallToolResult> GetProjectReferenceGraphAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var outcome = await InspectAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
        if (!outcome.Succeeded)
        {
            return Error(outcome);
        }

        var report = outcome.Report!;
        var projects = report.Projects
            .OrderBy(static project => project.Path, StringComparer.Ordinal)
            .Select(static project => new ProjectReferenceGraphEntry(
                project.Path,
                project.References,
                project.Diagnostics
                    .Where(static diagnostic =>
                        diagnostic.Code == InspectionDiagnosticCodes.ProjectReferenceUnresolved)
                    .ToArray()))
            .ToArray();

        return Success(new GetProjectReferenceGraphData(report.SchemaVersion, projects));
    }

    public async Task<CallToolResult> GetRepositoryDiagnosticsAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var outcome = await InspectAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
        if (!outcome.Succeeded)
        {
            return Error(outcome);
        }

        var report = outcome.Report!;
        var diagnostics = report.Diagnostics
            .Select(static diagnostic => new ContextualDiagnostic(null, diagnostic))
            .Concat(report.Projects
                .OrderBy(static project => project.Path, StringComparer.Ordinal)
                .SelectMany(static project => project.Diagnostics.Select(
                    diagnostic => new ContextualDiagnostic(project.Path, diagnostic))))
            .ToArray();

        return Success(new GetRepositoryDiagnosticsData(report.SchemaVersion, diagnostics));
    }

    public async Task<CallToolResult> GetSdkMetadataAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var outcome = await InspectAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
        if (!outcome.Succeeded)
        {
            return Error(outcome);
        }

        var report = outcome.Report!;
        return Success(new GetSdkMetadataData(report.SchemaVersion, report.DotNetSdk));
    }

    private Task<RepositoryInspectionOutcome> InspectAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken) =>
        _executor.ExecuteAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);

    private static CallToolResult Success<T>(T data) =>
        McpToolResults.Success(McpToolResults.SerializeToNode(data));

    private static CallToolResult Error(RepositoryInspectionOutcome outcome) =>
        McpToolResults.Error(outcome.ErrorCode!, outcome.ErrorMessage!);
}
