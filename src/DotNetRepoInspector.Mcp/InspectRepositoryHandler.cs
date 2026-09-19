using System.Text.Json;
using System.Text.Json.Nodes;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using ModelContextProtocol.Protocol;

namespace DotNetRepoInspector.Mcp;

public sealed class InspectRepositoryHandler
{
    private const string McpSchemaVersion = "1.0";

    private readonly IRepositoryInspector _repositoryInspector;
    private readonly RepositoryRoot _repositoryRoot;

    public InspectRepositoryHandler(
        IRepositoryInspector repositoryInspector,
        RepositoryRoot repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryInspector);
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        _repositoryInspector = repositoryInspector;
        _repositoryRoot = repositoryRoot;
    }

    public async Task<CallToolResult> ExecuteAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var validation = RepositoryPathBoundary.ValidateAndNormalize(
            _repositoryRoot,
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides);
        if (!validation.Succeeded)
        {
            return CreateError(validation.ErrorCode!, validation.ErrorMessage!);
        }

        try
        {
            var report = await _repositoryInspector.InspectAsync(
                new RepositoryInspectionRequest(
                    _repositoryRoot.FullPath,
                    ConfigurationPath: validation.ConfigurationPath,
                    DisableConfigurationFile: disableConfigurationFile,
                    ExcludedPaths: validation.ExcludedPaths,
                    ClassificationOverrides: validation.ClassificationOverrides),
                cancellationToken);

            return CreateSuccess(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            return CreateError(
                "inspection_failed",
                "Repository inspection failed before a report could be produced.");
        }
    }

    private static CallToolResult CreateSuccess(InspectionReport report)
    {
        var reportNode = JsonNode.Parse(InspectionJsonSerializer.Serialize(report));
        var response = new JsonObject
        {
            ["mcpSchemaVersion"] = McpSchemaVersion,
            ["ok"] = true,
            ["data"] = new JsonObject
            {
                ["report"] = reportNode
            },
            ["error"] = null
        };

        return CreateResult(response, isError: false);
    }

    internal static CallToolResult CreateError(string code, string message)
    {
        var response = new JsonObject
        {
            ["mcpSchemaVersion"] = McpSchemaVersion,
            ["ok"] = false,
            ["data"] = null,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["details"] = new JsonObject()
            }
        };

        return CreateResult(response, isError: true);
    }

    private static CallToolResult CreateResult(JsonNode response, bool isError)
    {
        var json = response.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = JsonSerializer.SerializeToElement(response),
            Content =
            [
                new TextContentBlock
                {
                    Text = json
                }
            ]
        };
    }
}
