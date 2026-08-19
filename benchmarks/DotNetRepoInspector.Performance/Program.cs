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
        using var timeoutSource = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.TimeoutSeconds));
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
        using var process = Process.GetCurrentProcess();

        var inspectionStopwatch = Stopwatch.StartNew();
        var report = await inspector.InspectAsync(repository.RootPath, timeoutSource.Token);
        inspectionStopwatch.Stop();

        var serializationStopwatch = Stopwatch.StartNew();
        var json = InspectionJsonSerializer.Serialize(report);
        serializationStopwatch.Stop();

        process.Refresh();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var discoveryMilliseconds = timedDiscoverer.Elapsed.TotalMilliseconds;
        var evaluationMilliseconds = timedEvaluator.Elapsed.TotalMilliseconds;
        var inspectionMilliseconds = inspectionStopwatch.Elapsed.TotalMilliseconds;
        var serializationMilliseconds = serializationStopwatch.Elapsed.TotalMilliseconds;

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
        File.WriteAllText(
            fullPath,
            $"{json}{Environment.NewLine}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

        var lines = new List<string>
        {
            "## DotNetRepoInspector performance",
            string.Empty,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Scenario: `{metrics.Scenario}` on `{metrics.OperatingSystem}` with `{metrics.Framework}` and SDK `{metrics.ResolvedSdkVersion ?? "unknown"}`."),
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Projects discovered | {metrics.DiscoveredProjectCount.ToString(CultureInfo.InvariantCulture)} |",
            $"| Project evaluations | {metrics.EvaluatedProjectCount.ToString(CultureInfo.InvariantCulture)} |",
            $"| Discovery | {FormatMilliseconds(metrics.DiscoveryMilliseconds)} |",
            $"| MSBuild evaluation | {FormatMilliseconds(metrics.EvaluationMilliseconds)} |",
            $"| Serialization | {FormatMilliseconds(metrics.SerializationMilliseconds)} |",
            $"| Other inspection overhead | {FormatMilliseconds(metrics.OtherInspectionMilliseconds)} |",
            $"| Inspection | {FormatMilliseconds(metrics.InspectionMilliseconds)} |",
            $"| End to end | {FormatMilliseconds(metrics.EndToEndMilliseconds)} |",
            $"| Managed allocations | {FormatBytes(metrics.ManagedAllocatedBytes)} |",
            $"| Peak working set | {FormatBytes(metrics.PeakWorkingSetBytes)} |",
            $"| JSON size | {FormatBytes(metrics.JsonBytes)} |",
            string.Empty,
            baselinePath is null
                ? "No regression baseline was supplied; this run records measurements only."
                : $"Regression limits loaded from `{baselinePath.Replace('\\', '/')}`."
        };

        File.WriteAllText(
            fullPath,
            $"{string.Join(Environment.NewLine, lines)}{Environment.NewLine}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PrintMetrics(PerformanceMetrics metrics)
    {
        Console.WriteLine($"Scenario: {metrics.Scenario}");
        Console.WriteLine(
            $"Projects: discovered={metrics.DiscoveredProjectCount.ToString(CultureInfo.InvariantCulture)}, evaluated={metrics.EvaluatedProjectCount.ToString(CultureInfo.InvariantCulture)}");
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
        var json = File.ReadAllText(Path.GetFullPath(path));
        return JsonSerializer.Deserialize<PerformanceBaseline>(json, SerializerOptions)
            ?? throw new InvalidDataException("Performance baseline could not be deserialized.");
    }

    private static List<string> EvaluateRegression(
        PerformanceMetrics metrics,
        PerformanceBaseline baseline)
    {
        if (baseline.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported performance baseline schema '{baseline.SchemaVersion.ToString(CultureInfo.InvariantCulture)}'.");
        }

        if (!string.Equals(metrics.Scenario, baseline.Scenario, StringComparison.Ordinal) ||
            metrics.ProjectCount != baseline.ProjectCount)
        {
            throw new InvalidDataException(
                $"Baseline scenario '{baseline.Scenario}' ({baseline.ProjectCount.ToString(CultureInfo.InvariantCulture)} projects) does not match '{metrics.Scenario}' ({metrics.ProjectCount.ToString(CultureInfo.InvariantCulture)} projects).");
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
        AddLimitFailure(
            failures,
            "Peak working set",
            metrics.PeakWorkingSetBytes,
            baseline.Limits.MaxPeakWorkingSetBytes,
            "bytes");

        if (metrics.DiscoveredProjectCount != metrics.ProjectCount)
        {
            failures.Add(
                $"Discovery returned {metrics.DiscoveredProjectCount.ToString(CultureInfo.InvariantCulture)} projects; expected {metrics.ProjectCount.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (metrics.EvaluatedProjectCount != metrics.ProjectCount)
        {
            failures.Add(
                $"MSBuild evaluator was invoked {metrics.EvaluatedProjectCount.ToString(CultureInfo.InvariantCulture)} times; expected exactly {metrics.ProjectCount.ToString(CultureInfo.InvariantCulture)} evaluations.");
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
    long MaxManagedAllocatedBytes,
    long MaxPeakWorkingSetBytes);
