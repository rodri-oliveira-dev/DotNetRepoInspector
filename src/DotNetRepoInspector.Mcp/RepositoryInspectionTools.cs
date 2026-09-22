using System.ComponentModel;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNetRepoInspector.Mcp;

[McpServerToolType]
public sealed class RepositoryInspectionTools
{
    [McpServerTool(
        Name = "inspect_repository",
        Title = "Inspect .NET repository",
        UseStructuredContent = true,
        OutputSchemaType = typeof(InspectRepositoryResponse),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Inspects the configured repository root and returns the canonical deterministic InspectionReport.")]
    public static Task<CallToolResult> InspectRepositoryAsync(
        InspectRepositoryHandler handler,
        [Description(
            "Optional path to a DotNetRepoInspector configuration file, relative to the configured repository root.")]
        string? configurationPath = null,
        [Description("Disables loading both the default and explicit configuration file.")]
        bool disableConfigurationFile = false,
        [Description(
            "Optional repository-relative project paths to exclude from inspection.")]
        string[]? excludedPaths = null,
        [Description(
            "Optional project classification overrides keyed by repository-relative project path.")]
        Dictionary<string, string>? classificationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return handler.ExecuteAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
    }

    [McpServerTool(
        Name = "list_projects",
        Title = "List .NET projects",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ListProjectsResponse),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Returns a compact deterministic index of projects in the configured repository root.")]
    public static Task<CallToolResult> ListProjectsAsync(
        GranularRepositoryToolsHandler handler,
        [Description("Optional repository-relative DotNetRepoInspector configuration path.")]
        string? configurationPath = null,
        [Description("Disables loading both the default and explicit configuration file.")]
        bool disableConfigurationFile = false,
        [Description("Optional repository-relative project paths to exclude.")]
        string[]? excludedPaths = null,
        [Description("Optional classification overrides keyed by repository-relative project path.")]
        Dictionary<string, string>? classificationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return handler.ListProjectsAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_project_details",
        Title = "Get .NET project details",
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetProjectDetailsResponse),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Returns the canonical details for one project selected by repository-relative path.")]
    public static Task<CallToolResult> GetProjectDetailsAsync(
        GranularRepositoryToolsHandler handler,
        [Description("Required repository-relative project path from list_projects.")]
        string projectPath,
        [Description("Optional repository-relative DotNetRepoInspector configuration path.")]
        string? configurationPath = null,
        [Description("Disables loading both the default and explicit configuration file.")]
        bool disableConfigurationFile = false,
        [Description("Optional repository-relative project paths to exclude.")]
        string[]? excludedPaths = null,
        [Description("Optional classification overrides keyed by repository-relative project path.")]
        Dictionary<string, string>? classificationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return handler.GetProjectDetailsAsync(
            projectPath,
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_project_reference_graph",
        Title = "Get project reference graph",
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetProjectReferenceGraphResponse),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Returns project-reference edges and unresolved-reference diagnostics for the repository.")]
    public static Task<CallToolResult> GetProjectReferenceGraphAsync(
        GranularRepositoryToolsHandler handler,
        [Description("Optional repository-relative DotNetRepoInspector configuration path.")]
        string? configurationPath = null,
        [Description("Disables loading both the default and explicit configuration file.")]
        bool disableConfigurationFile = false,
        [Description("Optional repository-relative project paths to exclude.")]
        string[]? excludedPaths = null,
        [Description("Optional classification overrides keyed by repository-relative project path.")]
        Dictionary<string, string>? classificationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return handler.GetProjectReferenceGraphAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_repository_diagnostics",
        Title = "Get repository diagnostics",
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetRepositoryDiagnosticsResponse),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Returns repository and project diagnostics with repository-relative project context.")]
    public static Task<CallToolResult> GetRepositoryDiagnosticsAsync(
        GranularRepositoryToolsHandler handler,
        [Description("Optional repository-relative DotNetRepoInspector configuration path.")]
        string? configurationPath = null,
        [Description("Disables loading both the default and explicit configuration file.")]
        bool disableConfigurationFile = false,
        [Description("Optional repository-relative project paths to exclude.")]
        string[]? excludedPaths = null,
        [Description("Optional classification overrides keyed by repository-relative project path.")]
        Dictionary<string, string>? classificationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return handler.GetRepositoryDiagnosticsAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_sdk_metadata",
        Title = "Get .NET SDK metadata",
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetSdkMetadataResponse),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Returns configured and resolved .NET SDK metadata for the repository.")]
    public static Task<CallToolResult> GetSdkMetadataAsync(
        GranularRepositoryToolsHandler handler,
        [Description("Optional repository-relative DotNetRepoInspector configuration path.")]
        string? configurationPath = null,
        [Description("Disables loading both the default and explicit configuration file.")]
        bool disableConfigurationFile = false,
        [Description("Optional repository-relative project paths to exclude.")]
        string[]? excludedPaths = null,
        [Description("Optional classification overrides keyed by repository-relative project path.")]
        Dictionary<string, string>? classificationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return handler.GetSdkMetadataAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
    }
}
