using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

namespace DotNetRepoInspector.Mcp;

internal static class McpToolContractFilters
{
    private static readonly Dictionary<string, HashSet<string>> ToolArguments =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["inspect_repository"] = InspectionArguments(),
            ["list_projects"] = InspectionArguments(),
            ["get_project_details"] = InspectionArguments("projectPath"),
            ["get_project_reference_graph"] = InspectionArguments(),
            ["get_repository_diagnostics"] = InspectionArguments(),
            ["get_sdk_metadata"] = InspectionArguments()
        };

    public static void Configure(IMcpRequestFilterBuilder filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            foreach (var tool in result.Tools.Where(static candidate =>
                         ToolArguments.ContainsKey(candidate.Name)))
            {
                var schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
                schema["additionalProperties"] = false;
                tool.InputSchema = JsonSerializer.SerializeToElement(schema);
            }

            return result;
        });

        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            if (context.Params?.Name is { } toolName &&
                ToolArguments.TryGetValue(toolName, out var arguments) &&
                context.Params.Arguments is not null &&
                context.Params.Arguments.Keys.Any(
                    name => !arguments.Contains(name)))
            {
                return InspectRepositoryHandler.CreateError(
                    "invalid_tool_input",
                    "The tool request contains an unknown input property.");
            }

            return await next(context, cancellationToken);
        });
    }

    private static HashSet<string> InspectionArguments(params string[] additionalArguments)
    {
        var arguments = new HashSet<string>(StringComparer.Ordinal)
        {
            "configurationPath",
            "disableConfigurationFile",
            "excludedPaths",
            "classificationOverrides"
        };
        arguments.UnionWith(additionalArguments);
        return arguments;
    }
}
