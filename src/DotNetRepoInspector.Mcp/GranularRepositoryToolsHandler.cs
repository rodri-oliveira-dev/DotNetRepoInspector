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

    public async Task<CallToolResult> ListIntegrationsAsync(
        bool? discoverIntegrations,
        string? projectPath,
        string? kind,
        string? direction,
        string? technology,
        int offset,
        int limit,
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > McpSecurityLimits.MaxIntegrationOffset ||
            limit < 1 || limit > McpSecurityLimits.MaxIntegrationPageSize)
        {
            return McpToolResults.Error(
                "invalid_tool_input",
                "Integration pagination is outside the supported bounds.");
        }

        string? normalizedProjectPath = null;
        if (projectPath is not null)
        {
            var validation = RepositoryPathBoundary.ValidateRelativeProjectPath(
                _executor.RepositoryRoot,
                projectPath);
            if (!validation.Succeeded)
            {
                return McpToolResults.Error(validation.ErrorCode!, validation.ErrorMessage!);
            }

            normalizedProjectPath = validation.NormalizedPath;
        }

        if ((kind is not null && !IntegrationKind.IsDefined(kind)) ||
            (direction is not null && !IntegrationDirection.IsDefined(direction)) ||
            (technology is not null && !IsSafeTechnology(technology)))
        {
            return McpToolResults.Error(
                "invalid_tool_input",
                "An integration filter contains an unsupported value.");
        }

        var outcome = await InspectAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            discoverIntegrations,
            cancellationToken);
        if (!outcome.Succeeded)
        {
            return Error(outcome);
        }

        var report = outcome.Report!;
        if (!report.IntegrationDiscovery.Enabled || !report.IntegrationDiscovery.Completed)
        {
            return McpToolResults.Error(
                "integration_discovery_not_enabled",
                "Integration Discovery was not enabled for this inspection. Set discoverIntegrations to true or enable it in repository configuration.");
        }

        IEnumerable<IntegrationFinding> filtered = report.Integrations;
        if (normalizedProjectPath is not null)
        {
            filtered = filtered.Where(finding => string.Equals(
                finding.ProjectPath,
                normalizedProjectPath,
                PathComparison));
        }

        if (kind is not null)
        {
            filtered = filtered.Where(finding => finding.Kind == kind);
        }

        if (direction is not null)
        {
            filtered = filtered.Where(finding => finding.Direction == direction);
        }

        if (technology is not null)
        {
            filtered = filtered.Where(finding => finding.Technology == technology);
        }

        IntegrationFinding[] findings = filtered.ToArray();
        IntegrationFinding[] page = findings.Skip(offset).Take(limit).ToArray();
        return Success(new ListIntegrationsData(
            report.SchemaVersion,
            offset,
            limit,
            findings.Length,
            offset + page.Length < findings.Length,
            report.IntegrationDiscovery.Truncated,
            page));
    }

    private Task<RepositoryInspectionOutcome> InspectAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken) =>
        InspectAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            discoverIntegrations: null,
            cancellationToken);

    private Task<RepositoryInspectionOutcome> InspectAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        bool? discoverIntegrations,
        CancellationToken cancellationToken) =>
        _executor.ExecuteAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            discoverIntegrations,
            cancellationToken);

    private static bool IsSafeTechnology(string value) =>
        value.Length is > 0 and <= 256 &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static CallToolResult Success<T>(T data) =>
        McpToolResults.Success(McpToolResults.SerializeToNode(data));

    private static CallToolResult Error(RepositoryInspectionOutcome outcome) =>
        McpToolResults.Error(outcome.ErrorCode!, outcome.ErrorMessage!);
}
