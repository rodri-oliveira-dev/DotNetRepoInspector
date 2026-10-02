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
        [Description("Explicitly enables or disables bounded Integration Discovery; repository configuration is used when omitted.")]
        bool? discoverIntegrations = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return handler.ExecuteAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            discoverIntegrations,
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

    [McpServerTool(
        Name = "list_integrations",
        Title = "List discovered integrations",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ListIntegrationsResponse),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Runs the shared bounded Integration Discovery pipeline and returns a filtered page of normalized findings.")]
    public static Task<CallToolResult> ListIntegrationsAsync(
        GranularRepositoryToolsHandler handler,
        [Description("Explicitly enables or disables bounded Integration Discovery; repository configuration is used when omitted.")]
        bool? discoverIntegrations = null,
        [Description("Optional exact repository-relative project path filter.")]
        string? projectPath = null,
        [Description("Optional integration kind filter.")]
        string? kind = null,
        [Description("Optional integration direction filter.")]
        string? direction = null,
        [Description("Optional normalized technology filter.")]
        string? technology = null,
        [Description("Zero-based result offset. Maximum 100000.")]
        int offset = 0,
        [Description("Maximum findings returned. Range 1 to 200; default 100.")]
        int limit = 100,
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
        return handler.ListIntegrationsAsync(
            discoverIntegrations,
            projectPath,
            kind,
            direction,
            technology,
            offset,
            limit,
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
    }
}
