using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace DotNetRepoInspector.Mcp;

internal static class McpToolResults
{
    public const string SchemaVersion = "1.0";

    public static CallToolResult Success(JsonNode data) =>
        Create(
            new JsonObject
            {
                ["mcpSchemaVersion"] = SchemaVersion,
                ["ok"] = true,
                ["data"] = data,
                ["error"] = null
            },
            isError: false);

    public static CallToolResult Error(string code, string message) =>
        Create(
            new JsonObject
            {
                ["mcpSchemaVersion"] = SchemaVersion,
                ["ok"] = false,
                ["data"] = null,
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message,
                    ["details"] = new JsonObject()
                }
            },
            isError: true);

    public static JsonNode SerializeToNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, McpJsonUtilities.DefaultOptions)!;

    private static CallToolResult Create(JsonNode response, bool isError)
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
