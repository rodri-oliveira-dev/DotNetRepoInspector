using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

namespace DotNetRepoInspector.Mcp;

internal static class McpToolContractFilters
{
    private static readonly HashSet<string> InspectRepositoryArguments =
        new(StringComparer.Ordinal)
        {
            "configurationPath",
            "disableConfigurationFile",
            "excludedPaths",
            "classificationOverrides"
        };

    public static void Configure(IMcpRequestFilterBuilder filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            var tool = result.Tools.SingleOrDefault(
                static candidate => candidate.Name == "inspect_repository");
            if (tool is not null)
            {
                var schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
                schema["additionalProperties"] = false;
                tool.InputSchema = JsonSerializer.SerializeToElement(schema);
            }

            return result;
        });

        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            if (context.Params?.Name == "inspect_repository" &&
                context.Params.Arguments is not null &&
                context.Params.Arguments.Keys.Any(
                    static name => !InspectRepositoryArguments.Contains(name)))
            {
                return InspectRepositoryHandler.CreateError(
                    "invalid_tool_input",
                    "The tool request contains an unknown input property.");
            }

            return await next(context, cancellationToken);
        });
    }
}
