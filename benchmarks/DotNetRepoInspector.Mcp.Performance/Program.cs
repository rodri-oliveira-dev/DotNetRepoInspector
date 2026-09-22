using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using DotNetRepoInspector.Engine;

using ModelContextProtocol.Client;

namespace DotNetRepoInspector.Mcp.Performance;

internal static class Program
{
    private const int RegressionExitCode = 2;

    public static async Task<int> Main(string[] args)
    {
        var options = BenchmarkOptions.Parse(args);
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var cancellationToken = timeoutSource.Token;
        var repositoryRoot = Path.GetFullPath(options.RepositoryRoot);

        var launchMilliseconds = await MeasureProcessLaunchAsync(repositoryRoot, cancellationToken);

        var engineWatch = Stopwatch.StartNew();
        var expected = await new RepositoryInspector().InspectAsync(repositoryRoot, cancellationToken);
        engineWatch.Stop();

        var cliMilliseconds = await MeasureCliAsync(repositoryRoot, cancellationToken);
        var standardError = new ConcurrentQueue<string>();
        var handshakeWatch = Stopwatch.StartNew();
        var client = await CreateClientAsync(repositoryRoot, standardError, cancellationToken);
        handshakeWatch.Stop();

        var toolMilliseconds = new Dictionary<string, double>(StringComparer.Ordinal);
        await using (client)
        {
            foreach (var toolName in new[]
            {
                "inspect_repository",
                "list_projects",
                "get_project_reference_graph",
                "get_repository_diagnostics",
                "get_sdk_metadata"
            })
            {
                toolMilliseconds[toolName] = await MeasureToolAsync(
                    client,
                    toolName,
                    new Dictionary<string, object?>(),
                    cancellationToken);
            }

            var firstProject = expected.Projects.OrderBy(static project => project.Path, StringComparer.Ordinal).First();
            toolMilliseconds["get_project_details"] = await MeasureToolAsync(
                client,
                "get_project_details",
                new Dictionary<string, object?> { ["projectPath"] = firstProject.Path },
                cancellationToken);
        }

        var metrics = new McpPerformanceMetrics(
            SchemaVersion: 1,
            Scenario: "project-kinds",
            ProjectCount: expected.Projects.Count,
            ProcessLaunchMilliseconds: launchMilliseconds,
            StartupAndHandshakeMilliseconds: handshakeWatch.Elapsed.TotalMilliseconds,
            EngineInspectionMilliseconds: engineWatch.Elapsed.TotalMilliseconds,
            CliInspectionMilliseconds: cliMilliseconds,
            ToolMilliseconds: toolMilliseconds,
            StructuredToolLogCount: standardError.Count(static line =>
                line.Contains("\"Tool\":", StringComparison.Ordinal) &&
                line.Contains("\"DurationMs\":", StringComparison.Ordinal) &&
                line.Contains("\"Status\":", StringComparison.Ordinal) &&
                line.Contains("\"CorrelationId\":", StringComparison.Ordinal)));

        WriteJson(options.OutputPath, metrics);
        WriteSummary(options.SummaryPath, metrics);
        PrintMetrics(metrics);

        if (options.BaselinePath is null)
        {
            return 0;
        }

        var baseline = JsonSerializer.Deserialize<McpPerformanceBaseline>(
            File.ReadAllText(Path.GetFullPath(options.BaselinePath)),
            SerializerOptions) ?? throw new InvalidDataException("MCP performance baseline is invalid.");
        var failures = Evaluate(metrics, baseline);
        if (failures.Count == 0)
        {
            Console.WriteLine("MCP performance regression guard passed.");
            return 0;
        }

        Console.Error.WriteLine("MCP performance regression guard failed:");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return RegressionExitCode;
    }

    private static async Task<double> MeasureProcessLaunchAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        using var process = CreateDotNetProcess(
            typeof(McpApplication).Assembly.Location,
            ["--root", repositoryRoot]);
        var watch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException("The MCP process could not be started.");
        }

        watch.Stop();
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != McpExitCodes.Success)
        {
            throw new InvalidOperationException("The MCP launch probe did not exit successfully.");
        }

        return watch.Elapsed.TotalMilliseconds;
    }

    private static async Task<double> MeasureCliAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"dri-cli-{Guid.NewGuid():N}.json");
        try
        {
            using var process = CreateDotNetProcess(
                typeof(DotNetRepoInspector.Cli.CliApplication).Assembly.Location,
                [repositoryRoot, "--no-config", "--output", outputPath]);
            var watch = Stopwatch.StartNew();
            if (!process.Start())
            {
                throw new InvalidOperationException("The CLI process could not be started.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            watch.Stop();
            await Task.WhenAll(standardOutput, standardError);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("The CLI performance probe did not exit successfully.");
            }

            return watch.Elapsed.TotalMilliseconds;
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static async Task<McpClient> CreateClientAsync(
        string repositoryRoot,
        ConcurrentQueue<string> standardError,
        CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DotNetRepoInspector MCP performance",
            Command = "dotnet",
            Arguments = [typeof(McpApplication).Assembly.Location, "--root", repositoryRoot],
            WorkingDirectory = repositoryRoot,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = standardError.Enqueue
        });
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static async Task<double> MeasureToolAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        watch.Stop();
        if (result.IsError == true)
        {
            throw new InvalidOperationException($"MCP tool '{toolName}' returned an error.");
        }

        return watch.Elapsed.TotalMilliseconds;
    }

    private static Process CreateDotNetProcess(string assemblyPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static List<string> Evaluate(
        McpPerformanceMetrics metrics,
        McpPerformanceBaseline baseline)
    {
        if (baseline.SchemaVersion != metrics.SchemaVersion ||
            !string.Equals(baseline.Scenario, metrics.Scenario, StringComparison.Ordinal))
        {
            throw new InvalidDataException("MCP performance scenario does not match its baseline.");
        }

        var failures = new List<string>();
        AddLimit(failures, "process launch", metrics.ProcessLaunchMilliseconds, baseline.Limits.MaxProcessLaunchMilliseconds);
        AddLimit(failures, "startup and handshake", metrics.StartupAndHandshakeMilliseconds, baseline.Limits.MaxStartupAndHandshakeMilliseconds);
        AddLimit(failures, "CLI inspection", metrics.CliInspectionMilliseconds, baseline.Limits.MaxCliToEngineRatio * metrics.EngineInspectionMilliseconds + baseline.Limits.FixedOverheadMilliseconds);
        foreach (var tool in metrics.ToolMilliseconds)
        {
            AddLimit(failures, tool.Key, tool.Value, baseline.Limits.MaxToolToEngineRatio * metrics.EngineInspectionMilliseconds + baseline.Limits.FixedOverheadMilliseconds);
        }

        if (metrics.StructuredToolLogCount != metrics.ToolMilliseconds.Count)
        {
            failures.Add("Each MCP tool call must emit exactly one structured completion event.");
        }

        return failures;
    }

    private static void AddLimit(List<string> failures, string metric, double actual, double maximum)
    {
        if (actual > maximum)
        {
            failures.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{metric} was {actual:F2} ms; limit is {maximum:F2} ms."));
        }
    }

    private static void WriteJson(string path, McpPerformanceMetrics metrics)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(metrics, SerializerOptions) + Environment.NewLine);
    }

    private static void WriteSummary(string? path, McpPerformanceMetrics metrics)
    {
        if (path is null)
        {
            return;
        }

        var lines = new List<string>
        {
            "## DotNetRepoInspector MCP performance",
            string.Empty,
            "| Metric | Duration |",
            "| --- | ---: |",
            $"| Process launch | {metrics.ProcessLaunchMilliseconds:F2} ms |",
            $"| Startup + handshake | {metrics.StartupAndHandshakeMilliseconds:F2} ms |",
            $"| Engine inspection | {metrics.EngineInspectionMilliseconds:F2} ms |",
            $"| CLI inspection | {metrics.CliInspectionMilliseconds:F2} ms |"
        };
        lines.AddRange(metrics.ToolMilliseconds.Select(static metric =>
            $"| MCP `{metric.Key}` | {metric.Value:F2} ms |"));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllLines(fullPath, lines);
    }

    private static void PrintMetrics(McpPerformanceMetrics metrics)
    {
        Console.WriteLine(JsonSerializer.Serialize(metrics, SerializerOptions));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

internal sealed record BenchmarkOptions(
    string RepositoryRoot,
    int TimeoutSeconds,
    string OutputPath,
    string? SummaryPath,
    string? BaselinePath)
{
    public static BenchmarkOptions Parse(IReadOnlyList<string> args)
    {
        string? repositoryRoot = null;
        var timeoutSeconds = 300;
        var outputPath = "artifacts/performance/mcp-metrics.json";
        string? summaryPath = null;
        string? baselinePath = null;
        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            var value = index + 1 < args.Count ? args[++index] : throw new ArgumentException($"Option '{option}' requires a value.");
            switch (option)
            {
                case "--repository":
                    repositoryRoot = value;
                    break;
                case "--timeout-seconds":
                    timeoutSeconds = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--output":
                    outputPath = value;
                    break;
                case "--summary":
                    summaryPath = value;
                    break;
                case "--baseline":
                    baselinePath = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown benchmark option '{option}'.");
            }
        }

        return new BenchmarkOptions(
            repositoryRoot ?? throw new ArgumentException("--repository is required."),
            timeoutSeconds,
            outputPath,
            summaryPath,
            baselinePath);
    }
}

internal sealed record McpPerformanceMetrics(
    int SchemaVersion,
    string Scenario,
    int ProjectCount,
    double ProcessLaunchMilliseconds,
    double StartupAndHandshakeMilliseconds,
    double EngineInspectionMilliseconds,
    double CliInspectionMilliseconds,
    IReadOnlyDictionary<string, double> ToolMilliseconds,
    int StructuredToolLogCount);

internal sealed record McpPerformanceBaseline(
    int SchemaVersion,
    string Scenario,
    McpPerformanceObserved Observed,
    McpPerformanceLimits Limits);

internal sealed record McpPerformanceObserved(
    double ProcessLaunchMilliseconds,
    double StartupAndHandshakeMilliseconds,
    double EngineInspectionMilliseconds,
    double CliInspectionMilliseconds,
    IReadOnlyDictionary<string, double> ToolMilliseconds);

internal sealed record McpPerformanceLimits(
    double MaxProcessLaunchMilliseconds,
    double MaxStartupAndHandshakeMilliseconds,
    double MaxCliToEngineRatio,
    double MaxToolToEngineRatio,
    double FixedOverheadMilliseconds);
