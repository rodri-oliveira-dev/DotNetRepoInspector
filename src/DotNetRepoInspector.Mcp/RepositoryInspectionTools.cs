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
}
