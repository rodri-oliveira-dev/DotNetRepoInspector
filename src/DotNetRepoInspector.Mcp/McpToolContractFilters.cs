using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetRepoInspector.Mcp;

internal static class McpToolContractFilters
{
    private static readonly Action<ILogger, string, double, string, string, Exception?>
        LogToolCompleted = LoggerMessage.Define<string, double, string, string>(
            LogLevel.Information,
            new EventId(3100, "McpToolCompleted"),
            "MCP tool completed Tool={Tool} DurationMs={DurationMs} Status={Status} CorrelationId={CorrelationId}");

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
            var toolName = context.Params?.Name ?? "unknown";
            var correlationId = Guid.NewGuid().ToString("N");
            var stopwatch = Stopwatch.StartNew();
            var services = context.Services ?? context.Server.Services ??
                throw new InvalidOperationException("MCP request services are unavailable.");
            var logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("DotNetRepoInspector.Mcp.Tools");
            try
            {
                if (ToolArguments.TryGetValue(toolName, out var arguments) &&
                    context.Params?.Arguments is not null &&
                    context.Params.Arguments.Keys.Any(
                        name => !arguments.Contains(name)))
                {
                    LogCompletion(logger, toolName, correlationId, stopwatch, "error");
                    return InspectRepositoryHandler.CreateError(
                        "invalid_tool_input",
                        "The tool request contains an unknown input property.");
                }

                var result = await next(context, cancellationToken);
                LogCompletion(
                    logger,
                    toolName,
                    correlationId,
                    stopwatch,
                    result.IsError == true ? "error" : "success");
                return result;
            }
            catch (OperationCanceledException)
            {
                LogCompletion(logger, toolName, correlationId, stopwatch, "cancelled");
                throw;
            }
            catch
            {
                LogCompletion(logger, toolName, correlationId, stopwatch, "failed");
                throw;
            }
        });
    }

    private static void LogCompletion(
        ILogger logger,
        string toolName,
        string correlationId,
        Stopwatch stopwatch,
        string status)
    {
        stopwatch.Stop();
        LogToolCompleted(
            logger,
            toolName,
            stopwatch.Elapsed.TotalMilliseconds,
            status,
            correlationId,
            null);
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
