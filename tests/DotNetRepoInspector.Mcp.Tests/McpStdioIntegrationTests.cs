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
        var tool = Assert.Single(tools);
        Assert.Equal("inspect_repository", tool.Name);
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

        Assert.Contains(
            standardError,
            static line => line.Contains("started", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InspectRepository_RejectsUnknownInputProperties()
    {
        await using var client = await CreateClientAsync(
            FixturePath("EmptyRepository"),
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "inspect_repository",
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
        await using var client = await CreateClientAsync(
            fixturePath,
            new ConcurrentQueue<string>(),
            TestContext.Current.CancellationToken);
        var expected = await new RepositoryInspector().InspectAsync(
            new RepositoryInspectionRequest(fixturePath),
            TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "inspect_repository",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
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

        Assert.False(result.IsError);
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
        CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DotNetRepoInspector MCP integration test",
            Command = ServerExecutablePath(),
            Arguments = ["--root", repositoryRoot],
            WorkingDirectory = repositoryRoot,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = standardError.Enqueue
        });

        return await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);
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
