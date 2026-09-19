using System.Text.Json;
using System.Text.Json.Serialization;

using DotNetRepoInspector.Engine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace DotNetRepoInspector.Mcp;

public static class McpApplication
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter? standardError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        standardError ??= Console.Error;
        var parseResult = McpStartupOptions.Parse(args);
        if (!parseResult.Succeeded || parseResult.Options is null)
        {
            await standardError.WriteLineAsync(parseResult.Error);
            return McpExitCodes.InvalidArguments;
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = Array.Empty<string>()
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton(parseResult.Options.RepositoryRoot);
        builder.Services.AddSingleton<IRepositoryInspector, RepositoryInspector>();
        builder.Services.AddSingleton<RepositoryInspectionExecutor>();
        builder.Services.AddSingleton<InspectRepositoryHandler>();
        builder.Services.AddSingleton<GranularRepositoryToolsHandler>();
        var toolJsonOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = McpProductInfo.ServerName,
                    Version = McpProductInfo.Version
                };
            })
            .WithStdioServerTransport()
            .WithRequestFilters(McpToolContractFilters.Configure)
            .WithTools<RepositoryInspectionTools>(toolJsonOptions);

        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return McpExitCodes.Success;
    }
}
