using System.Text.Json.Nodes;

using DotNetRepoInspector.Core.Contracts;

using ModelContextProtocol.Protocol;

namespace DotNetRepoInspector.Mcp;

public sealed class InspectRepositoryHandler
{
    private readonly RepositoryInspectionExecutor _executor;

    public InspectRepositoryHandler(
        RepositoryInspectionExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    public async Task<CallToolResult> ExecuteAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var outcome = await _executor.ExecuteAsync(
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides,
            cancellationToken);
        if (!outcome.Succeeded)
        {
            return CreateError(outcome.ErrorCode!, outcome.ErrorMessage!);
        }

        return CreateSuccess(outcome.Report!);
    }

    private static CallToolResult CreateSuccess(InspectionReport report)
    {
        var reportNode = JsonNode.Parse(InspectionJsonSerializer.Serialize(report));
        var response = new JsonObject
        {
            ["report"] = reportNode
        };

        return McpToolResults.Success(response);
    }

    internal static CallToolResult CreateError(string code, string message)
    {
        return McpToolResults.Error(code, message);
    }
}
