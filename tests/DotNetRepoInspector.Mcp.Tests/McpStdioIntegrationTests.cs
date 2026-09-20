using System.Collections.Concurrent;
using System.Diagnostics;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using ModelContextProtocol.Client;

using Xunit;

namespace DotNetRepoInspector.Mcp.Tests;

public sealed class McpStdioIntegrationTests
{
    [Fact]
    public async Task Server_CompletesHandshakeAndPublishesExpectedMetadataAndToolSchema()
    {
        var standardError = new ConcurrentQueue<string>();
        await using var client = await CreateClientAsync(
            FixturePath("ProjectKinds"),
            standardError,
            TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(McpProductInfo.ServerName, client.ServerInfo.Name);
        Assert.Equal(McpProductInfo.Version, client.ServerInfo.Version);
        Assert.NotNull(client.ServerCapabilities.Tools);
        Assert.Equal(
            [
                "get_project_details",
                "get_project_reference_graph",
                "get_repository_diagnostics",
                "get_sdk_metadata",
                "inspect_repository",
                "list_projects"
            ],
            tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal));
        var tool = tools.Single(static candidate => candidate.Name == "inspect_repository");
        Assert.Contains("canonical", tool.Description, StringComparison.OrdinalIgnoreCase);

        var inputSchema = tool.ProtocolTool.InputSchema;
        Assert.Equal("object", inputSchema.GetProperty("type").GetString());
        var properties = inputSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("configurationPath", out _));
        Assert.True(properties.TryGetProperty("disableConfigurationFile", out _));
        Assert.True(properties.TryGetProperty("excludedPaths", out _));
        Assert.True(properties.TryGetProperty("classificationOverrides", out _));
        Assert.False(inputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.NotNull(tool.ProtocolTool.OutputSchema);

        foreach (var publishedTool in tools)
        {
            Assert.False(
                publishedTool.ProtocolTool.InputSchema
                    .GetProperty("additionalProperties")
                    .GetBoolean());
            Assert.NotNull(publishedTool.ProtocolTool.OutputSchema);
            Assert.True(publishedTool.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(publishedTool.ProtocolTool.Annotations?.DestructiveHint);
            Assert.True(publishedTool.ProtocolTool.Annotations?.IdempotentHint);
            Assert.False(publishedTool.ProtocolTool.Annotations?.OpenWorldHint);
        }

        var detailsSchema = tools
            .Single(static candidate => candidate.Name == "get_project_details")
            .ProtocolTool.InputSchema;
        Assert.Contains(
            "projectPath",
            detailsSchema.GetProperty("required").EnumerateArray()
                .Select(static item => item.GetString()));

        Assert.Contains(
            standardError,
            static line => line.Contains("started", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GranularTools_ReturnFocusedFactsEquivalentToEngine()
    {
        var fixturePath = FixturePath("ProjectKinds");
        var standardError = new ConcurrentQueue<string>();
        await using var client = await CreateClientAsync(
            fixturePath,
            standardError,
            TestContext.Current.CancellationToken);
        var expected = await new RepositoryInspector().InspectAsync(
            new RepositoryInspectionRequest(fixturePath),
            TestContext.Current.CancellationToken);

        var projectsResult = await CallAsync(client, "list_projects");
        var projectsData = SuccessData(projectsResult);
        Assert.Equal(expected.SchemaVersion, projectsData.GetProperty("inspectionSchemaVersion").GetString());
        Assert.Equal(expected.Projects.Count, projectsData.GetProperty("projects").GetArrayLength());
        Assert.True(
            projectsResult.StructuredContent!.Value.GetRawText().Length <
            InspectionJsonSerializer.Serialize(expected).Length);

        var projectPath = expected.Projects[0].Path;
        var detailsResult = await client.CallToolAsync(
            "get_project_details",
            new Dictionary<string, object?> { ["projectPath"] = projectPath },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            projectPath,
            SuccessData(detailsResult).GetProperty("project").GetProperty("path").GetString());

        var graphData = SuccessData(await CallAsync(client, "get_project_reference_graph"));
        Assert.Equal(expected.Projects.Count, graphData.GetProperty("projects").GetArrayLength());

        var diagnosticsData = SuccessData(await CallAsync(client, "get_repository_diagnostics"));
        Assert.Equal(
            expected.Diagnostics.Count + expected.Projects.Sum(static project => project.Diagnostics.Count),
            diagnosticsData.GetProperty("diagnostics").GetArrayLength());

        var sdkData = SuccessData(await CallAsync(client, "get_sdk_metadata"));
        Assert.Equal(
            expected.DotNetSdk.ResolvedVersion,
            sdkData.GetProperty("dotNetSdk").GetProperty("resolvedVersion").GetString());
    }

    [Fact]
    public async Task GranularTools_ReturnDeterministicEmptyCollectionsForEmptyRepository()
    {
        await using var client = await CreateClientAsync(
            FixturePath("EmptyRepository"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, SuccessData(await CallAsync(client, "list_projects"))
            .GetProperty("projects").GetArrayLength());
        Assert.Equal(0, SuccessData(await CallAsync(client, "get_project_reference_graph"))
            .GetProperty("projects").GetArrayLength());
        Assert.Equal(0, SuccessData(await CallAsync(client, "get_repository_diagnostics"))
            .GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task GetProjectDetails_ReturnsPredictableErrorsForMissingAndOutsideProjects()
    {
        var standardError = new ConcurrentQueue<string>();
        await using var client = await CreateClientAsync(
            FixturePath("ProjectKinds"),
            standardError,
            TestContext.Current.CancellationToken);

        var missing = await client.CallToolAsync(
            "get_project_details",
            new Dictionary<string, object?> { ["projectPath"] = "Missing/Missing.csproj" },
            cancellationToken: TestContext.Current.CancellationToken);
        AssertToolError(missing, "project_not_found");

        var outside = await client.CallToolAsync(
            "get_project_details",
            new Dictionary<string, object?> { ["projectPath"] = "../Outside.csproj" },
            cancellationToken: TestContext.Current.CancellationToken);
        AssertToolError(outside, "path_outside_repository_root");
        var errorLogs = standardError.Where(static line =>
            line.Contains("\"Tool\":\"get_project_details\"", StringComparison.Ordinal) &&
            line.Contains("\"Status\":\"error\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, errorLogs.Length);
        Assert.All(errorLogs, line => Assert.DoesNotContain(
            FixturePath("ProjectKinds"),
            line,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProjectDetails_RejectsMissingRequiredProjectPath()
    {
        var standardError = new ConcurrentQueue<string>();
        await using var client = await CreateClientAsync(
            FixturePath("EmptyRepository"),
            standardError,
            TestContext.Current.CancellationToken);

        var result = await CallAsync(client, "get_project_details");

        Assert.True(result.IsError);
        Assert.Contains(
            "projectPath",
            string.Join(Environment.NewLine, standardError),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceGraph_PreservesUnresolvedReferencesAsPartialResults()
    {
        await using var client = await CreateClientAsync(
            FixturePath("ProjectReferences/Unresolved"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);

        var data = SuccessData(await CallAsync(client, "get_project_reference_graph"));
        var project = Assert.Single(data.GetProperty("projects").EnumerateArray());
        Assert.Equal("Missing/Missing.csproj", Assert.Single(
            project.GetProperty("references").EnumerateArray())
            .GetProperty("path").GetString());
        Assert.Equal(
            InspectionDiagnosticCodes.ProjectReferenceUnresolved,
            Assert.Single(project.GetProperty("diagnostics").EnumerateArray())
                .GetProperty("code").GetString());
    }

    [Fact]
    public async Task SdkAndDiagnosticsTools_PreserveMissingSdkAsPartialResult()
    {
        await using var client = await CreateClientAsync(
            FixturePath("Compatibility/MissingSdk"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);

        var sdkData = SuccessData(await CallAsync(client, "get_sdk_metadata"));
        Assert.True(
            sdkData.TryGetProperty("dotNetSdk", out var sdkMetadata),
            sdkData.GetRawText());
        Assert.False(sdkMetadata.TryGetProperty("resolvedVersion", out _));

        var diagnostics = SuccessData(await CallAsync(client, "get_repository_diagnostics"))
            .GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Contains(diagnostics, static item =>
            item.GetProperty("diagnostic").GetProperty("code").GetString() ==
            InspectionDiagnosticCodes.DotNetSdkUnavailable);
    }

    [Fact]
    public async Task Server_PropagatesProtocolCancellationAndRemainsResponsive()
    {
        await using var client = await CreateClientAsync(
            FixturePath("ProjectKinds"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.CallToolAsync(
                "inspect_repository",
                new Dictionary<string, object?>(),
                cancellationToken: cancellationSource.Token));

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(tools, static tool => tool.Name == "inspect_repository");
    }

    [Fact]
    public async Task Server_DoesNotExposeInheritedSecretInProtocolOrStandardLogs()
    {
        const string secret = "mcp-security-secret-must-not-leak";
        var standardError = new ConcurrentQueue<string>();
        await using var client = await CreateClientAsync(
            FixturePath("EmptyRepository"),
            standardError,
            TestContext.Current.CancellationToken,
            new Dictionary<string, string?>
            {
                ["DRI_SECURITY_TEST_ACCESS_TOKEN"] = secret
            });

        var result = await CallAsync(client, "inspect_repository");

        Assert.DoesNotContain(
            secret,
            result.StructuredContent!.Value.GetRawText(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            standardError,
            line => line.Contains(secret, StringComparison.Ordinal));
        Assert.Contains(standardError, static line =>
            line.Contains("\"Tool\":\"inspect_repository\"", StringComparison.Ordinal) &&
            line.Contains("\"Status\":\"success\"", StringComparison.Ordinal) &&
            line.Contains("\"CorrelationId\":", StringComparison.Ordinal) &&
            line.Contains("\"DurationMs\":", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("inspect_repository")]
    [InlineData("list_projects")]
    [InlineData("get_project_details")]
    [InlineData("get_project_reference_graph")]
    [InlineData("get_repository_diagnostics")]
    [InlineData("get_sdk_metadata")]
    public async Task Tools_RejectUnknownInputProperties(string toolName)
    {
        await using var client = await CreateClientAsync(
            FixturePath("EmptyRepository"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            toolName,
            new Dictionary<string, object?>
            {
                ["command"] = "arbitrary-command"
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var text = Assert.Single(result.Content).ToString();
        Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectRepository_ReturnsReportEquivalentToEngineForRealFixture()
    {
        var fixturePath = FixturePath("ProjectKinds");
        var standardError = new ConcurrentQueue<string>();
        await using var client = await CreateClientAsync(
            fixturePath,
            standardError,
            TestContext.Current.CancellationToken);
        var expected = await new RepositoryInspector().InspectAsync(
            new RepositoryInspectionRequest(fixturePath),
            TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "inspect_repository",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(
            result.IsError,
            string.Join(
                Environment.NewLine,
                result.Content.Select(static content => content.ToString()).Concat(standardError)));
        Assert.NotNull(result.StructuredContent);
        var envelope = result.StructuredContent.Value;
        Assert.Equal("1.0", envelope.GetProperty("mcpSchemaVersion").GetString());
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        var actualJson = envelope
            .GetProperty("data")
            .GetProperty("report")
            .GetRawText();
        var actual = InspectionJsonSerializer.Deserialize(actualJson);
        Assert.Equal(
            InspectionJsonSerializer.Serialize(expected),
            InspectionJsonSerializer.Serialize(actual));
        Assert.Equal(6, actual.Projects.Count);
        Assert.Contains(actual.Projects, static project => project.Classification?.Kind == "web");
        Assert.Contains(actual.Projects, static project => project.Classification?.Kind == "worker");
        Assert.Contains(actual.Projects, static project => project.References is not null);
    }

    [Fact]
    public async Task InspectRepository_PreservesInvalidProjectDiagnostics()
    {
        await using var client = await CreateClientAsync(
            FixturePath("InvalidProject"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "inspect_repository",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError, string.Join(Environment.NewLine, result.Content));
        var reportJson = result.StructuredContent!.Value
            .GetProperty("data")
            .GetProperty("report")
            .GetRawText();
        var report = InspectionJsonSerializer.Deserialize(reportJson);
        Assert.Contains(
            report.Projects.SelectMany(static project => project.Diagnostics),
            static diagnostic => diagnostic.Severity == InspectionDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ListProjects_PreservesPartialProjectDiagnosticCounts()
    {
        await using var client = await CreateClientAsync(
            FixturePath("InvalidProject"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);

        var projects = SuccessData(await CallAsync(client, "list_projects"))
            .GetProperty("projects").EnumerateArray().ToArray();

        var project = Assert.Single(projects);
        Assert.True(project.GetProperty("diagnosticCount").GetInt32() > 0);
        Assert.True(project.GetProperty("errorCount").GetInt32() > 0);
    }

    [Fact]
    public async Task Server_InvalidRootWritesOnlyToStderrAndReturnsPredictableExitCode()
    {
        var missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"DotNetRepoInspector-Missing-{Guid.NewGuid():N}");
        using var process = StartServerProcess(missingRoot);
        var standardOutput = await process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(McpExitCodes.InvalidArguments, process.ExitCode);
        Assert.Equal(string.Empty, standardOutput);
        Assert.Contains("does not exist", standardError, StringComparison.Ordinal);
        Assert.DoesNotContain(missingRoot, standardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_ExitsGracefullyWhenStdinCloses()
    {
        using var process = StartServerProcess(FixturePath("EmptyRepository"));

        process.StandardInput.Close();
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeoutSource.Token);

        Assert.True(process.HasExited);
        Assert.Equal(McpExitCodes.Success, process.ExitCode);
        Assert.Equal(
            string.Empty,
            await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<McpClient> CreateClientAsync(
        string repositoryRoot,
        ConcurrentQueue<string> standardError,
        CancellationToken cancellationToken,
        IDictionary<string, string?>? environmentVariables = null)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DotNetRepoInspector MCP integration test",
            Command = ServerExecutablePath(),
            Arguments = ["--root", repositoryRoot],
            WorkingDirectory = repositoryRoot,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = standardError.Enqueue,
            EnvironmentVariables = environmentVariables
        });

        return await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);
    }

    private static ValueTask<ModelContextProtocol.Protocol.CallToolResult> CallAsync(
        McpClient client,
        string toolName) =>
        client.CallToolAsync(
            toolName,
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

    private static System.Text.Json.JsonElement SuccessData(
        ModelContextProtocol.Protocol.CallToolResult result)
    {
        Assert.False(result.IsError, string.Join(Environment.NewLine, result.Content));
        var envelope = result.StructuredContent!.Value;
        Assert.Equal("1.0", envelope.GetProperty("mcpSchemaVersion").GetString());
        Assert.True(envelope.GetProperty("ok").GetBoolean());
        return envelope.GetProperty("data");
    }

    private static void AssertToolError(
        ModelContextProtocol.Protocol.CallToolResult result,
        string expectedCode)
    {
        Assert.True(result.IsError);
        var envelope = result.StructuredContent!.Value;
        Assert.False(envelope.GetProperty("ok").GetBoolean());
        Assert.Equal(expectedCode, envelope.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("stack", envelope.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static Process StartServerProcess(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo(ServerExecutablePath())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--root");
        startInfo.ArgumentList.Add(repositoryRoot);

        var process = new Process
        {
            StartInfo = startInfo
        };
        Assert.True(process.Start());
        return process;
    }

    private static string ServerExecutablePath()
    {
        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return Path.Combine(
            AppContext.BaseDirectory,
            $"DotNetRepoInspector.Mcp{extension}");
    }

    private static string FixturePath(string relativePath) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
}
