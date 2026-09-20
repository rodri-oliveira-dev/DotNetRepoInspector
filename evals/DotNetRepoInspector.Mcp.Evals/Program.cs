using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

using ModelContextProtocol.Client;

namespace DotNetRepoInspector.Mcp.Evals;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        var options = EvalOptions.Parse(args);
        if (!options.IsValid)
        {
            Console.Error.WriteLine(options.Error);
            Console.Error.WriteLine("Usage: dotnet run --project evals/DotNetRepoInspector.Mcp.Evals -- --server <command-or-path> --fixtures <path> [--server-arguments-json <json-array> | --server-arguments-file <path>] [--dataset <path>] [--output <dir>] [--client <name>] [--provider <name>] [--model <name>] [--client-version <version>]");
            return 2;
        }

        var dataset = await LoadDatasetAsync(options.DatasetPath);
        Directory.CreateDirectory(options.OutputDirectory);

        var run = await ExecuteAsync(options, dataset);
        var jsonPath = Path.Combine(options.OutputDirectory, $"{run.RunId}.json");
        var markdownPath = Path.Combine(options.OutputDirectory, $"{run.RunId}.md");

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(run, JsonOptions),
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            markdownPath,
            ReportWriter.ToMarkdown(run),
            Encoding.UTF8);

        Console.WriteLine($"JSON report: {jsonPath}");
        Console.WriteLine($"Markdown report: {markdownPath}");
        Console.WriteLine($"Completed {run.Summary.CompletedCases}/{run.Summary.TotalCases} eval cases.");

        return run.Summary.FailedCases == 0 ? 0 : 1;
    }

    private static async Task<EvalDataset> LoadDatasetAsync(string datasetPath)
    {
        await using var stream = File.OpenRead(datasetPath);
        return await JsonSerializer.DeserializeAsync<EvalDataset>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The eval dataset could not be read.");
    }

    private static async Task<EvalRunReport> ExecuteAsync(EvalOptions options, EvalDataset dataset)
    {
        var results = new List<EvalCaseResult>();
        var toolNames = new SortedSet<string>(StringComparer.Ordinal);
        string? serverName = null;
        string? serverVersion = null;

        foreach (var evalCase in dataset.Cases)
        {
            var fixtureRoot = Path.GetFullPath(Path.Combine(options.FixturesDirectory, evalCase.FixturePath));
            var caseResult = await ExecuteCaseAsync(options, evalCase, fixtureRoot);
            results.Add(caseResult);

            foreach (var toolName in caseResult.DiscoveredTools)
            {
                toolNames.Add(toolName);
            }

            serverName ??= caseResult.ServerName;
            serverVersion ??= caseResult.ServerVersion;
        }

        var summary = EvalSummary.From(results);
        var metadata = new EvalRunMetadata(
            DateTimeOffset.UtcNow,
            Environment.OSVersion.ToString(),
            RuntimeInformation(),
            options.ServerPath,
            options.FixturesDirectory,
            dataset.Id,
            dataset.SchemaVersion,
            options.ClientName,
            options.Provider,
            options.Model,
            options.ClientVersion,
            "stdio",
            "2025-06-18",
            serverName,
            serverVersion,
            toolNames.ToArray());

        return new EvalRunReport(
            $"mcp-evals-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            metadata,
            summary,
            results);
    }

    private static async Task<EvalCaseResult> ExecuteCaseAsync(
        EvalOptions options,
        EvalCase evalCase,
        string fixtureRoot)
    {
        using var caseTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var stopwatch = Stopwatch.StartNew();
        var stderr = new ConcurrentQueue<string>();
        var calledTools = new List<string>();
        var assertionResults = new List<AssertionResult>();

        try
        {
            if (!Directory.Exists(fixtureRoot))
            {
                return EvalCaseResult.Failed(
                    evalCase,
                    stopwatch.ElapsedMilliseconds,
                    "Fixture root does not exist.",
                    [],
                    calledTools,
                    assertionResults);
            }

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "DotNetRepoInspector MCP eval runner",
                Command = options.ServerPath,
                Arguments = [.. options.ServerArguments, "--root", fixtureRoot],
                WorkingDirectory = fixtureRoot,
                ShutdownTimeout = TimeSpan.FromSeconds(10),
                StandardErrorLines = stderr.Enqueue
            });

            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: caseTimeout.Token);
            var tools = await client.ListToolsAsync(cancellationToken: caseTimeout.Token);
            var discoveredTools = tools.Select(static tool => tool.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (!discoveredTools.Contains(evalCase.ExpectedTool, StringComparer.Ordinal))
            {
                return EvalCaseResult.Failed(
                    evalCase,
                    stopwatch.ElapsedMilliseconds,
                    $"Expected tool '{evalCase.ExpectedTool}' was not discovered.",
                    discoveredTools,
                    calledTools,
                    assertionResults,
                    client.ServerInfo.Name,
                    client.ServerInfo.Version);
            }

            calledTools.Add(evalCase.ExpectedTool);
            var result = await client.CallToolAsync(
                evalCase.ExpectedTool,
                new Dictionary<string, object?>(),
                cancellationToken: caseTimeout.Token);

            if (result.IsError == true || !result.StructuredContent.HasValue)
            {
                return EvalCaseResult.Failed(
                    evalCase,
                    stopwatch.ElapsedMilliseconds,
                    "The expected tool returned an MCP error or no structured content.",
                    discoveredTools,
                    calledTools,
                    assertionResults,
                    client.ServerInfo.Name,
                    client.ServerInfo.Version);
            }

            var envelope = result.StructuredContent.Value;
            foreach (var assertion in evalCase.Assertions)
            {
                assertionResults.Add(AssertFact(assertion, envelope));
            }

            return EvalCaseResult.Succeeded(
                evalCase,
                stopwatch.ElapsedMilliseconds,
                discoveredTools,
                calledTools,
                assertionResults,
                client.ServerInfo.Name,
                client.ServerInfo.Version);
        }
        catch (OperationCanceledException) when (caseTimeout.IsCancellationRequested)
        {
            return EvalCaseResult.Failed(
                evalCase,
                stopwatch.ElapsedMilliseconds,
                "Evaluation case timed out after two minutes.",
                [],
                calledTools,
                assertionResults);
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidOperationException or
            TimeoutException or
            JsonException or
            global::ModelContextProtocol.McpException)
        {
            return EvalCaseResult.Failed(
                evalCase,
                stopwatch.ElapsedMilliseconds,
                exception.Message,
                [],
                calledTools,
                assertionResults);
        }
    }

    private static AssertionResult AssertFact(EvalAssertion assertion, JsonElement envelope)
    {
        try
        {
            var data = envelope.GetProperty("data");
            return assertion.Kind switch
            {
                "project_target_frameworks" => AssertStringArray(
                    assertion,
                    FindProject(data, assertion.ProjectPath)
                        .GetProperty("targetFrameworks")),
                "project_classification" => AssertString(
                    assertion,
                    FindProject(data, assertion.ProjectPath)
                        .GetProperty("classification")
                        .GetProperty("kind")),
                "project_reference" => AssertStringArray(
                    assertion,
                    FindProject(data, assertion.ProjectPath)
                        .GetProperty("references")
                        .EnumerateArray()
                        .Select(static item => item.GetProperty("path").GetString())
                        .Where(static value => value is not null)
                        .Cast<string>()),
                "diagnostic_code" => AssertContains(
                    assertion,
                    FindDiagnostics(data)
                        .Select(static item => item.GetProperty("code").GetString())
                        .Where(static value => value is not null)
                        .Cast<string>()),
                "sdk_configured_version" => AssertString(
                    assertion,
                    data.GetProperty("dotNetSdk")
                        .GetProperty("configured")
                        .GetProperty("version")),
                _ => AssertionResult.Fail(assertion, $"Unsupported assertion kind '{assertion.Kind}'.")
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or JsonException)
        {
            return AssertionResult.Fail(assertion, exception.Message);
        }
    }

    private static AssertionResult AssertString(EvalAssertion assertion, JsonElement actual)
    {
        var actualValue = actual.GetString();
        return string.Equals(actualValue, assertion.Value, StringComparison.Ordinal)
            ? AssertionResult.Pass(assertion, actualValue)
            : AssertionResult.Fail(assertion, $"Expected '{assertion.Value}', got '{actualValue}'.");
    }

    private static AssertionResult AssertStringArray(EvalAssertion assertion, JsonElement actual) =>
        AssertStringArray(
            assertion,
            actual.EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static value => value is not null)
                .Cast<string>());

    private static AssertionResult AssertStringArray(EvalAssertion assertion, IEnumerable<string> actual)
    {
        var expected = assertion.Values ?? [];
        var actualArray = actual.Order(StringComparer.Ordinal).ToArray();
        var expectedArray = expected.Order(StringComparer.Ordinal).ToArray();

        return actualArray.SequenceEqual(expectedArray, StringComparer.Ordinal)
            ? AssertionResult.Pass(assertion, string.Join(", ", actualArray))
            : AssertionResult.Fail(
                assertion,
                $"Expected [{string.Join(", ", expectedArray)}], got [{string.Join(", ", actualArray)}].");
    }

    private static AssertionResult AssertContains(EvalAssertion assertion, IEnumerable<string> actual)
    {
        var actualArray = actual.ToArray();
        return actualArray.Contains(assertion.Value, StringComparer.Ordinal)
            ? AssertionResult.Pass(assertion, assertion.Value)
            : AssertionResult.Fail(
                assertion,
                $"Expected to find '{assertion.Value}' in [{string.Join(", ", actualArray)}].");
    }

    private static JsonElement FindProject(JsonElement data, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new InvalidOperationException("The assertion requires projectPath.");
        }

        foreach (var project in EnumerateProjects(data))
        {
            if (string.Equals(
                project.GetProperty("path").GetString(),
                projectPath,
                StringComparison.Ordinal))
            {
                return project;
            }
        }

        throw new InvalidOperationException($"Project '{projectPath}' was not found.");
    }

    private static IEnumerable<JsonElement> EnumerateProjects(JsonElement data)
    {
        if (data.TryGetProperty("project", out var singleProject))
        {
            yield return singleProject;
        }

        if (data.TryGetProperty("projects", out var projects))
        {
            foreach (var project in projects.EnumerateArray())
            {
                yield return project;
            }
        }

        if (data.TryGetProperty("report", out var report) &&
            report.TryGetProperty("projects", out var reportProjects))
        {
            foreach (var project in reportProjects.EnumerateArray())
            {
                yield return project;
            }
        }
    }

    private static IEnumerable<JsonElement> FindDiagnostics(JsonElement data)
    {
        if (data.TryGetProperty("diagnostics", out var diagnostics))
        {
            foreach (var diagnostic in diagnostics.EnumerateArray())
            {
                yield return diagnostic.TryGetProperty("diagnostic", out var nested)
                    ? nested
                    : diagnostic;
            }
        }

        foreach (var project in EnumerateProjects(data))
        {
            if (!project.TryGetProperty("diagnostics", out var projectDiagnostics))
            {
                continue;
            }

            foreach (var diagnostic in projectDiagnostics.EnumerateArray())
            {
                yield return diagnostic;
            }
        }

        if (data.TryGetProperty("report", out var report))
        {
            foreach (var diagnostic in FindDiagnostics(report))
            {
                yield return diagnostic;
            }
        }
    }

    private static string RuntimeInformation() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
}

internal sealed record EvalOptions(
    string ServerPath,
    IReadOnlyList<string> ServerArguments,
    string FixturesDirectory,
    string DatasetPath,
    string OutputDirectory,
    string ClientName,
    string Provider,
    string? Model,
    string? ClientVersion,
    string? Error)
{
    public bool IsValid => Error is null;

    public static EvalOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                index + 1 >= args.Length)
            {
                continue;
            }

            values[args[index][2..]] = args[++index];
        }

        var baseDirectory = AppContext.BaseDirectory;
        var dataset = values.TryGetValue("dataset", out var datasetPath)
            ? datasetPath
            : Path.Combine(baseDirectory, "Dataset", "mcp-evals-v1.json");
        var output = values.TryGetValue("output", out var outputPath)
            ? outputPath
            : Path.Combine("artifacts", "mcp-evals");

        values.TryGetValue("server", out var server);
        values.TryGetValue("server-arguments-json", out var serverArgumentsJson);
        values.TryGetValue("server-arguments-file", out var serverArgumentsFile);
        values.TryGetValue("fixtures", out var fixtures);
        values.TryGetValue("client", out var client);
        values.TryGetValue("provider", out var provider);
        values.TryGetValue("model", out var model);
        values.TryGetValue("client-version", out var clientVersion);

        if (string.IsNullOrWhiteSpace(server))
        {
            return Invalid("Missing required --server path.");
        }

        if (string.IsNullOrWhiteSpace(fixtures))
        {
            return Invalid("Missing required --fixtures path.");
        }

        if (!string.IsNullOrWhiteSpace(serverArgumentsFile))
        {
            if (!File.Exists(serverArgumentsFile))
            {
                return Invalid($"Server arguments file does not exist: {serverArgumentsFile}");
            }

            serverArgumentsJson = File.ReadAllText(serverArgumentsFile);
        }

        var serverArguments = ParseServerArguments(serverArgumentsJson);
        if (serverArguments is null)
        {
            return Invalid("--server-arguments-json must be a JSON array of strings.");
        }

        if (File.Exists(server))
        {
            server = Path.GetFullPath(server);
        }
        fixtures = Path.GetFullPath(fixtures);
        dataset = Path.GetFullPath(dataset);
        output = Path.GetFullPath(output);

        if (!Directory.Exists(fixtures))
        {
            return Invalid($"Fixtures directory does not exist: {fixtures}");
        }

        if (!File.Exists(dataset))
        {
            return Invalid($"Dataset file does not exist: {dataset}");
        }

        return new EvalOptions(
            server,
            serverArguments,
            fixtures,
            dataset,
            output,
            client ?? "mcp-sdk-deterministic",
            provider ?? "protocol",
            model,
            clientVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            null);

        static EvalOptions Invalid(string error) =>
            new(
                string.Empty,
                [],
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                null,
                error);

        static IReadOnlyList<string>? ParseServerArguments(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<string[]>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

internal sealed record EvalDataset(
    int SchemaVersion,
    string Id,
    string Description,
    IReadOnlyList<EvalCase> Cases);

internal sealed record EvalCase(
    string Id,
    string Question,
    string FixturePath,
    string ExpectedTool,
    IReadOnlyList<EvalAssertion> Assertions);

internal sealed record EvalAssertion(
    string Kind,
    string? ProjectPath,
    string? Value,
    IReadOnlyList<string>? Values);

internal sealed record AssertionResult(
    string Kind,
    string? ProjectPath,
    bool Passed,
    string? Expected,
    string? Actual,
    string? Failure)
{
    public static AssertionResult Pass(EvalAssertion assertion, string? actual) =>
        new(
            assertion.Kind,
            assertion.ProjectPath,
            true,
            assertion.Value ?? (assertion.Values is null ? null : string.Join(", ", assertion.Values)),
            actual,
            null);

    public static AssertionResult Fail(EvalAssertion assertion, string failure) =>
        new(
            assertion.Kind,
            assertion.ProjectPath,
            false,
            assertion.Value ?? (assertion.Values is null ? null : string.Join(", ", assertion.Values)),
            null,
            failure);
}

internal sealed record EvalCaseMetrics(
    double TaskCompletion,
    double ToolSelection,
    int UnnecessaryCalls,
    double FactualFidelity,
    double Groundedness,
    int UnsupportedClaims);

internal sealed record EvalCaseResult(
    string Id,
    string Question,
    string FixturePath,
    string ExpectedTool,
    string? ServerName,
    string? ServerVersion,
    long DurationMs,
    bool Completed,
    string? Failure,
    IReadOnlyList<string> DiscoveredTools,
    IReadOnlyList<string> CalledTools,
    IReadOnlyList<AssertionResult> Assertions,
    EvalCaseMetrics Metrics)
{
    public static EvalCaseResult Succeeded(
        EvalCase evalCase,
        long durationMs,
        IReadOnlyList<string> discoveredTools,
        IReadOnlyList<string> calledTools,
        IReadOnlyList<AssertionResult> assertions,
        string? serverName,
        string? serverVersion)
    {
        var passedAssertions = assertions.Count(static assertion => assertion.Passed);
        var completion = assertions.Count > 0 && passedAssertions == assertions.Count ? 1.0 : 0.0;
        var fidelity = assertions.Count == 0 ? 0.0 : (double)passedAssertions / assertions.Count;
        var toolSelection = calledTools.SequenceEqual([evalCase.ExpectedTool], StringComparer.Ordinal) ? 1.0 : 0.0;
        var unnecessaryCalls = Math.Max(0, calledTools.Count - 1);
        var unsupportedClaims = assertions.Count(static assertion => !assertion.Passed);

        return new EvalCaseResult(
            evalCase.Id,
            evalCase.Question,
            evalCase.FixturePath,
            evalCase.ExpectedTool,
            serverName,
            serverVersion,
            durationMs,
            completion == 1.0,
            completion == 1.0 ? null : "One or more deterministic assertions failed.",
            discoveredTools,
            calledTools,
            assertions,
            new EvalCaseMetrics(
                completion,
                toolSelection,
                unnecessaryCalls,
                fidelity,
                completion,
                unsupportedClaims));
    }

    public static EvalCaseResult Failed(
        EvalCase evalCase,
        long durationMs,
        string failure,
        IReadOnlyList<string> discoveredTools,
        IReadOnlyList<string> calledTools,
        IReadOnlyList<AssertionResult> assertions,
        string? serverName = null,
        string? serverVersion = null) =>
        new(
            evalCase.Id,
            evalCase.Question,
            evalCase.FixturePath,
            evalCase.ExpectedTool,
            serverName,
            serverVersion,
            durationMs,
            false,
            failure,
            discoveredTools,
            calledTools,
            assertions,
            new EvalCaseMetrics(
                0.0,
                0.0,
                calledTools.Count,
                0.0,
                0.0,
                assertions.Count(static assertion => !assertion.Passed)));
}

internal sealed record EvalRunMetadata(
    DateTimeOffset TimestampUtc,
    string OperatingSystem,
    string Runtime,
    string ServerPath,
    string FixturesDirectory,
    string DatasetId,
    int DatasetSchemaVersion,
    string Client,
    string Provider,
    string? Model,
    string? ClientVersion,
    string Transport,
    string McpProtocolVersion,
    string? ServerName,
    string? ServerVersion,
    IReadOnlyList<string> DiscoveredTools);

internal sealed record EvalSummary(
    int TotalCases,
    int CompletedCases,
    int FailedCases,
    double TaskCompletion,
    double ToolSelection,
    double FactualFidelity,
    double Groundedness,
    int UnnecessaryCalls,
    int UnsupportedClaims)
{
    public static EvalSummary From(IReadOnlyList<EvalCaseResult> results)
    {
        var total = results.Count;
        return new EvalSummary(
            total,
            results.Count(static result => result.Completed),
            results.Count(static result => !result.Completed),
            Average(results, static result => result.Metrics.TaskCompletion),
            Average(results, static result => result.Metrics.ToolSelection),
            Average(results, static result => result.Metrics.FactualFidelity),
            Average(results, static result => result.Metrics.Groundedness),
            results.Sum(static result => result.Metrics.UnnecessaryCalls),
            results.Sum(static result => result.Metrics.UnsupportedClaims));
    }

    private static double Average(
        IReadOnlyCollection<EvalCaseResult> results,
        Func<EvalCaseResult, double> selector) =>
        results.Count == 0 ? 0.0 : Math.Round(results.Average(selector), 4);
}

internal sealed record EvalRunReport(
    string RunId,
    EvalRunMetadata Metadata,
    EvalSummary Summary,
    IReadOnlyList<EvalCaseResult> Cases);

internal static class ReportWriter
{
    public static string ToMarkdown(EvalRunReport report)
    {
        var builder = new StringBuilder();
        AppendLine(builder, $"# MCP eval report: {report.RunId}");
        builder.AppendLine();
        builder.AppendLine("## Metadata");
        builder.AppendLine();
        AppendLine(builder, $"- Timestamp UTC: `{report.Metadata.TimestampUtc:O}`");
        AppendLine(builder, $"- Dataset: `{report.Metadata.DatasetId}` schema `{report.Metadata.DatasetSchemaVersion}`");
        AppendLine(builder, $"- Client/provider/model: `{report.Metadata.Client}` / `{report.Metadata.Provider}` / `{report.Metadata.Model ?? "n/a"}`");
        AppendLine(builder, $"- Client version: `{report.Metadata.ClientVersion ?? "n/a"}`");
        AppendLine(builder, $"- Transport: `{report.Metadata.Transport}`");
        AppendLine(builder, $"- MCP protocol version: `{report.Metadata.McpProtocolVersion}`");
        AppendLine(builder, $"- Server: `{report.Metadata.ServerName ?? "unknown"}` `{report.Metadata.ServerVersion ?? "unknown"}`");
        AppendLine(builder, $"- Runtime: `{report.Metadata.Runtime}`");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | ---: |");
        AppendLine(builder, $"| Cases completed | {report.Summary.CompletedCases}/{report.Summary.TotalCases} |");
        AppendLine(builder, $"| Task completion | {report.Summary.TaskCompletion:P2} |");
        AppendLine(builder, $"| Tool selection | {report.Summary.ToolSelection:P2} |");
        AppendLine(builder, $"| Factual fidelity | {report.Summary.FactualFidelity:P2} |");
        AppendLine(builder, $"| Groundedness | {report.Summary.Groundedness:P2} |");
        AppendLine(builder, $"| Unnecessary calls | {report.Summary.UnnecessaryCalls} |");
        AppendLine(builder, $"| Unsupported claims | {report.Summary.UnsupportedClaims} |");
        builder.AppendLine();
        builder.AppendLine("## Cases");
        builder.AppendLine();
        builder.AppendLine("| Case | Tool | Status | Assertions | Duration |");
        builder.AppendLine("| --- | --- | --- | ---: | ---: |");

        foreach (var result in report.Cases)
        {
            var passed = result.Assertions.Count(static assertion => assertion.Passed);
            AppendLine(
                builder,
                $"| `{result.Id}` | `{result.ExpectedTool}` | {(result.Completed ? "pass" : "fail")} | {passed}/{result.Assertions.Count} | {result.DurationMs} ms |");
        }

        builder.AppendLine();
        builder.AppendLine("## Failures");
        builder.AppendLine();

        var failures = report.Cases.Where(static result => !result.Completed).ToArray();
        if (failures.Length == 0)
        {
            builder.AppendLine("No deterministic failures.");
        }
        else
        {
            foreach (var failure in failures)
            {
                AppendLine(builder, $"- `{failure.Id}`: {failure.Failure}");
            }
        }

        return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, FormattableString value) =>
        builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));
}
