using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;
using DotNetRepoInspector.Git;
using DotNetRepoInspector.MSBuild.Discovery;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.Sdk;

namespace DotNetRepoInspector.Performance;

internal static class Program
{
    private const int RegressionExitCode = 2;

    public static async Task<int> Main(string[] args)
    {
        var options = BenchmarkOptions.Parse(args);
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var repository = SyntheticRepository.Create(options.ProjectCount);

        var timedDiscoverer = new TimedProjectDiscoverer(new FileSystemProjectDiscoverer());
        var timedEvaluator = new TimedProjectFactsEvaluator(new MsBuildProjectFactsEvaluator());
        var inspector = new RepositoryInspector(
            timedDiscoverer,
            timedEvaluator,
            new DotNetSdkInspector(),
            new GitRepositoryMetadataProvider());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var process = Process.GetCurrentProcess();

        var inspectionStopwatch = Stopwatch.StartNew();
        var report = await inspector.InspectAsync(repository.RootPath, timeoutSource.Token);
        inspectionStopwatch.Stop();

        var serializationStopwatch = Stopwatch.StartNew();
        var json = InspectionJsonSerializer.Serialize(report);
        serializationStopwatch.Stop();

        process.Refresh();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var inspectionMilliseconds = inspectionStopwatch.Elapsed.TotalMilliseconds;
        var serializationMilliseconds = serializationStopwatch.Elapsed.TotalMilliseconds;
        var discoveryMilliseconds = timedDiscoverer.Elapsed.TotalMilliseconds;
        var evaluationMilliseconds = timedEvaluator.Elapsed.TotalMilliseconds;
        var metrics = new PerformanceMetrics(
            SchemaVersion: 1,
            Scenario: $"synthetic-{options.ProjectCount}-projects",
            ProjectCount: options.ProjectCount,
            DiscoveredProjectCount: report.Projects.Count,
            EvaluatedProjectCount: timedEvaluator.EvaluationCount,
            ResolvedSdkVersion: report.DotNetSdk.ResolvedVersion,
            DiscoveryMilliseconds: discoveryMilliseconds,
            EvaluationMilliseconds: evaluationMilliseconds,
            SerializationMilliseconds: serializationMilliseconds,
            InspectionMilliseconds: inspectionMilliseconds,
            OtherInspectionMilliseconds: Math.Max(
                0,
                inspectionMilliseconds - discoveryMilliseconds - evaluationMilliseconds),
            EndToEndMilliseconds: inspectionMilliseconds + serializationMilliseconds,
            ManagedAllocatedBytes: Math.Max(0, allocatedAfter - allocatedBefore),
            PeakWorkingSetBytes: process.PeakWorkingSet64,
            JsonBytes: Encoding.UTF8.GetByteCount(json),
            OperatingSystem: RuntimeInformation.OSDescription,
            Framework: RuntimeInformation.FrameworkDescription);

        WriteJson(options.OutputPath, metrics);
        WriteSummary(options.SummaryPath, metrics, options.BaselinePath);
        PrintMetrics(metrics);

        if (options.BaselinePath is null)
        {
            return 0;
        }

        var baseline = ReadBaseline(options.BaselinePath);
        var failures = EvaluateRegression(metrics, baseline);
        if (failures.Count == 0)
        {
            Console.WriteLine("Performance regression guard passed.");
            return 0;
        }

        Console.Error.WriteLine("Performance regression guard failed:");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return RegressionExitCode;
    }

    private static void WriteJson(string outputPath, PerformanceMetrics metrics)
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = JsonSerializer.Serialize(metrics, SerializerOptions);
        File.WriteAllText(fullPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static void WriteSummary(
        string? summaryPath,
        PerformanceMetrics metrics,
        string? baselinePath)
    {
        if (summaryPath is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(summaryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var builder = new StringBuilder();
        builder.AppendLine("## DotNetRepoInspector performance");
        builder.AppendLine();
        builder.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Scenario: `{metrics.Scenario}` on `{metrics.OperatingSystem}` with `{metrics.Framework}` and SDK `{metrics.ResolvedSdkVersion ?? "unknown"}`."));
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | ---: |");
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Projects discovered | {metrics.DiscoveredProjectCount} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Project evaluations | {metrics.EvaluatedProjectCount} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Discovery | {FormatMilliseconds(metrics.DiscoveryMilliseconds)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| MSBuild evaluation | {FormatMilliseconds(metrics.EvaluationMilliseconds)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Serialization | {FormatMilliseconds(metrics.SerializationMilliseconds)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Other inspection overhead | {FormatMilliseconds(metrics.OtherInspectionMilliseconds)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Inspection | {FormatMilliseconds(metrics.InspectionMilliseconds)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| End to end | {FormatMilliseconds(metrics.EndToEndMilliseconds)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Managed allocations | {FormatBytes(metrics.ManagedAllocatedBytes)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| Peak working set | {FormatBytes(metrics.PeakWorkingSetBytes)} |"));
        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| JSON size | {FormatBytes(metrics.JsonBytes)} |"));
        builder.AppendLine();
        builder.AppendLine(baselinePath is null
            ? "No regression baseline was supplied; this run records measurements only."
            : $"Regression limits loaded from `{baselinePath.Replace('\\', '/')}`.");

        File.WriteAllText(fullPath, builder.ToString(), new UTF8Encoding(false));
    }

    private static void PrintMetrics(PerformanceMetrics metrics)
    {
        Console.WriteLine($"Scenario: {metrics.Scenario}");
        Console.WriteLine($"Projects: discovered={metrics.DiscoveredProjectCount}, evaluated={metrics.EvaluatedProjectCount}");
        Console.WriteLine($"Discovery: {FormatMilliseconds(metrics.DiscoveryMilliseconds)}");
        Console.WriteLine($"MSBuild evaluation: {FormatMilliseconds(metrics.EvaluationMilliseconds)}");
        Console.WriteLine($"Serialization: {FormatMilliseconds(metrics.SerializationMilliseconds)}");
        Console.WriteLine($"Other inspection overhead: {FormatMilliseconds(metrics.OtherInspectionMilliseconds)}");
        Console.WriteLine($"Inspection: {FormatMilliseconds(metrics.InspectionMilliseconds)}");
        Console.WriteLine($"End to end: {FormatMilliseconds(metrics.EndToEndMilliseconds)}");
        Console.WriteLine($"Managed allocations: {FormatBytes(metrics.ManagedAllocatedBytes)}");
        Console.WriteLine($"Peak working set: {FormatBytes(metrics.PeakWorkingSetBytes)}");
        Console.WriteLine($"JSON size: {FormatBytes(metrics.JsonBytes)}");
    }

    private static PerformanceBaseline ReadBaseline(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize<PerformanceBaseline>(json, SerializerOptions)
            ?? throw new InvalidDataException("Performance baseline could not be deserialized.");
    }

    private static List<string> EvaluateRegression(
        PerformanceMetrics metrics,
        PerformanceBaseline baseline)
    {
        if (baseline.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported performance baseline schema '{baseline.SchemaVersion}'.");
        }

        if (!string.Equals(metrics.Scenario, baseline.Scenario, StringComparison.Ordinal) ||
            metrics.ProjectCount != baseline.ProjectCount)
        {
            throw new InvalidDataException(
                $"Baseline scenario '{baseline.Scenario}' ({baseline.ProjectCount} projects) does not match '{metrics.Scenario}' ({metrics.ProjectCount} projects).");
        }

        var failures = new List<string>();
        AddLimitFailure(
            failures,
            "MSBuild evaluation",
            metrics.EvaluationMilliseconds,
            baseline.Limits.MaxEvaluationMilliseconds,
            "ms");
        AddLimitFailure(
            failures,
            "End to end",
            metrics.EndToEndMilliseconds,
            baseline.Limits.MaxEndToEndMilliseconds,
            "ms");
        AddLimitFailure(
            failures,
            "Managed allocations",
            metrics.ManagedAllocatedBytes,
            baseline.Limits.MaxManagedAllocatedBytes,
            "bytes");

        if (metrics.DiscoveredProjectCount != metrics.ProjectCount)
        {
            failures.Add(
                $"Discovery returned {metrics.DiscoveredProjectCount} projects; expected {metrics.ProjectCount}.");
        }

        if (metrics.EvaluatedProjectCount != metrics.ProjectCount)
        {
            failures.Add(
                $"MSBuild evaluator was invoked {metrics.EvaluatedProjectCount} times; expected exactly {metrics.ProjectCount} evaluations.");
        }

        return failures;
    }

    private static void AddLimitFailure(
        List<string> failures,
        string metricName,
        double actual,
        double maximum,
        string unit)
    {
        if (actual <= maximum)
        {
            return;
        }

        failures.Add(
            $"{metricName} was {actual.ToString("F2", CultureInfo.InvariantCulture)} {unit}; limit is {maximum.ToString("F2", CultureInfo.InvariantCulture)} {unit}.");
    }

    private static string FormatMilliseconds(double value) =>
        $"{value.ToString("F2", CultureInfo.InvariantCulture)} ms";

    private static string FormatBytes(long value)
    {
        const double mebibyte = 1024d * 1024d;
        return $"{(value / mebibyte).ToString("F2", CultureInfo.InvariantCulture)} MiB";
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

internal sealed record BenchmarkOptions(
    int ProjectCount,
    int TimeoutSeconds,
    string OutputPath,
    string? SummaryPath,
    string? BaselinePath)
{
    public static BenchmarkOptions Parse(IReadOnlyList<string> args)
    {
        var projectCount = 100;
        var timeoutSeconds = 300;
        var outputPath = "artifacts/performance/metrics.json";
        string? summaryPath = null;
        string? baselinePath = null;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--project-count":
                    projectCount = ParsePositiveInt(ReadValue(args, ref index, argument), argument);
                    break;
                case "--timeout-seconds":
                    timeoutSeconds = ParsePositiveInt(ReadValue(args, ref index, argument), argument);
                    break;
                case "--output":
                    outputPath = ReadValue(args, ref index, argument);
                    break;
                case "--summary":
                    summaryPath = ReadValue(args, ref index, argument);
                    break;
                case "--baseline":
                    baselinePath = ReadValue(args, ref index, argument);
                    break;
                default:
                    throw new ArgumentException($"Unknown benchmark option '{argument}'.");
            }
        }

        return new BenchmarkOptions(
            projectCount,
            timeoutSeconds,
            outputPath,
            summaryPath,
            baselinePath);
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return args[++index];
    }

    private static int ParsePositiveInt(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0
            ? result
            : throw new ArgumentException($"Option '{option}' requires a positive integer.");
}

internal sealed class TimedProjectDiscoverer(IProjectDiscoverer inner) : IProjectDiscoverer
{
    public TimeSpan Elapsed { get; private set; }

    public IReadOnlyList<DiscoveredProject> Discover(ProjectDiscoveryRequest request) =>
        Measure(() => inner.Discover(request));

    public IReadOnlyList<DiscoveredProject> Discover(
        ProjectDiscoveryRequest request,
        CancellationToken cancellationToken) =>
        Measure(() => inner.Discover(request, cancellationToken));

    private IReadOnlyList<DiscoveredProject> Measure(Func<IReadOnlyList<DiscoveredProject>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            stopwatch.Stop();
            Elapsed += stopwatch.Elapsed;
        }
    }
}

internal sealed class TimedProjectFactsEvaluator(IMsBuildProjectFactsEvaluator inner)
    : IMsBuildProjectFactsEvaluator
{
    public TimeSpan Elapsed { get; private set; }

    public int EvaluationCount { get; private set; }

    public async Task<MsBuildProjectFactsResult> EvaluateAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        EvaluationCount++;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await inner.EvaluateAsync(projectPath, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            Elapsed += stopwatch.Elapsed;
        }
    }
}

internal sealed class SyntheticRepository : IDisposable
{
    private SyntheticRepository(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static SyntheticRepository Create(int projectCount)
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"DotNetRepoInspector-Performance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);

        File.WriteAllText(
            Path.Combine(rootPath, "global.json"),
            """
            {
              "sdk": {
                "version": "10.0.100",
                "rollForward": "latestFeature",
                "allowPrerelease": false
              }
            }
            """,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(rootPath, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """,
            new UTF8Encoding(false));

        var sourceRoot = Path.Combine(rootPath, "src");
        Directory.CreateDirectory(sourceRoot);

        for (var index = 0; index < projectCount; index++)
        {
            var projectName = $"Project{index:D4}";
            var projectDirectory = Path.Combine(sourceRoot, projectName);
            Directory.CreateDirectory(projectDirectory);

            var projectReference = index == 0
                ? string.Empty
                : $"""
                    <ItemGroup>
                      <ProjectReference Include="../Project{index - 1:D4}/Project{index - 1:D4}.csproj" />
                    </ItemGroup>
                  """;

            var projectContent = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                {projectReference}
                </Project>
                """;

            File.WriteAllText(
                Path.Combine(projectDirectory, $"{projectName}.csproj"),
                projectContent,
                new UTF8Encoding(false));
        }

        return new SyntheticRepository(rootPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException)
        {
            // Benchmark results are more valuable than cleanup failures in the temporary directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Benchmark results are more valuable than cleanup failures in the temporary directory.
        }
    }
}

internal sealed record PerformanceMetrics(
    int SchemaVersion,
    string Scenario,
    int ProjectCount,
    int DiscoveredProjectCount,
    int EvaluatedProjectCount,
    string? ResolvedSdkVersion,
    double DiscoveryMilliseconds,
    double EvaluationMilliseconds,
    double SerializationMilliseconds,
    double InspectionMilliseconds,
    double OtherInspectionMilliseconds,
    double EndToEndMilliseconds,
    long ManagedAllocatedBytes,
    long PeakWorkingSetBytes,
    int JsonBytes,
    string OperatingSystem,
    string Framework);

internal sealed record PerformanceBaseline(
    int SchemaVersion,
    string Scenario,
    int ProjectCount,
    PerformanceObserved Observed,
    PerformanceLimits Limits);

internal sealed record PerformanceObserved(
    double EvaluationMilliseconds,
    double EndToEndMilliseconds,
    long ManagedAllocatedBytes,
    long PeakWorkingSetBytes);

internal sealed record PerformanceLimits(
    double MaxEvaluationMilliseconds,
    double MaxEndToEndMilliseconds,
    long MaxManagedAllocatedBytes);
