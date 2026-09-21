using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Mcp.Tests;

public sealed class GranularRepositoryToolsHandlerTests
{
    [Fact]
    public async Task GranularTools_ProjectCanonicalFactsFromInspectionReport()
    {
        var handler = CreateHandler(CreateReport());
        var cancellationToken = TestContext.Current.CancellationToken;

        var list = await handler.ListProjectsAsync(null, false, null, null, cancellationToken);
        var projects = Data(list).GetProperty("projects");
        Assert.Equal(2, projects.GetArrayLength());
        Assert.Equal("src/App/App.csproj", projects[0].GetProperty("path").GetString());
        Assert.Equal(1, projects[0].GetProperty("errorCount").GetInt32());
        Assert.Equal(0, projects[0].GetProperty("warningCount").GetInt32());
        Assert.Equal("src/Lib/Lib.csproj", projects[1].GetProperty("path").GetString());
        Assert.Equal(1, projects[1].GetProperty("warningCount").GetInt32());

        var details = await handler.GetProjectDetailsAsync(
            "src/App/App.csproj",
            null,
            false,
            null,
            null,
            cancellationToken);
        Assert.Equal(
            "App",
            Data(details).GetProperty("project").GetProperty("name").GetString());

        var graph = await handler.GetProjectReferenceGraphAsync(
            null,
            false,
            null,
            null,
            cancellationToken);
        var graphProjects = Data(graph).GetProperty("projects");
        Assert.Equal(
            "src/Lib/Lib.csproj",
            graphProjects[0]
                .GetProperty("references")[0]
                .GetProperty("path")
                .GetString());
        Assert.Single(graphProjects[0].GetProperty("diagnostics").EnumerateArray());

        var diagnostics = await handler.GetRepositoryDiagnosticsAsync(
            null,
            false,
            null,
            null,
            cancellationToken);
        Assert.Equal(3, Data(diagnostics).GetProperty("diagnostics").GetArrayLength());

        var sdk = await handler.GetSdkMetadataAsync(
            null,
            false,
            null,
            null,
            cancellationToken);
        Assert.Equal(
            "10.0.401",
            Data(sdk).GetProperty("dotNetSdk").GetProperty("resolvedVersion").GetString());
    }

    [Fact]
    public async Task GetProjectDetails_ReturnsStableErrorsForInvalidAndMissingProjects()
    {
        var handler = CreateHandler(CreateReport());
        var cancellationToken = TestContext.Current.CancellationToken;

        var invalid = await handler.GetProjectDetailsAsync(
            "../outside.csproj",
            null,
            false,
            null,
            null,
            cancellationToken);
        Assert.Equal("path_outside_repository_root", ErrorCode(invalid));

        var missing = await handler.GetProjectDetailsAsync(
            "src/Missing/Missing.csproj",
            null,
            false,
            null,
            null,
            cancellationToken);
        Assert.Equal("project_not_found", ErrorCode(missing));
    }

    [Fact]
    public async Task RepositoryInspectionTools_DelegateEveryPublicTool()
    {
        var report = CreateReport();
        var granular = CreateHandler(report);
        var inspect = new InspectRepositoryHandler(new RepositoryInspectionExecutor(
            StubInspector.Returning(report),
            new RepositoryRoot(Path.GetFullPath("."))));
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.False((await RepositoryInspectionTools.InspectRepositoryAsync(
            inspect,
            cancellationToken: cancellationToken)).IsError);
        Assert.False((await RepositoryInspectionTools.ListProjectsAsync(
            granular,
            cancellationToken: cancellationToken)).IsError);
        Assert.False((await RepositoryInspectionTools.GetProjectDetailsAsync(
            granular,
            "src/App/App.csproj",
            cancellationToken: cancellationToken)).IsError);
        Assert.False((await RepositoryInspectionTools.GetProjectReferenceGraphAsync(
            granular,
            cancellationToken: cancellationToken)).IsError);
        Assert.False((await RepositoryInspectionTools.GetRepositoryDiagnosticsAsync(
            granular,
            cancellationToken: cancellationToken)).IsError);
        Assert.False((await RepositoryInspectionTools.GetSdkMetadataAsync(
            granular,
            cancellationToken: cancellationToken)).IsError);
    }

    [Fact]
    public void Constructors_RejectNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new GranularRepositoryToolsHandler(null!));
    }

    private static GranularRepositoryToolsHandler CreateHandler(InspectionReport report) =>
        new(new RepositoryInspectionExecutor(
            StubInspector.Returning(report),
            new RepositoryRoot(Path.GetFullPath("."))));

    private static System.Text.Json.JsonElement Data(ModelContextProtocol.Protocol.CallToolResult result)
    {
        Assert.False(result.IsError);
        return result.StructuredContent!.Value.GetProperty("data");
    }

    private static string? ErrorCode(ModelContextProtocol.Protocol.CallToolResult result)
    {
        Assert.True(result.IsError);
        return result.StructuredContent!.Value
            .GetProperty("error")
            .GetProperty("code")
            .GetString();
    }

    private static InspectionReport CreateReport()
    {
        var unresolved = new InspectionDiagnostic(
            InspectionDiagnosticCodes.ProjectReferenceUnresolved,
            InspectionDiagnosticSeverity.Error,
            "Reference is unresolved.",
            "MSBuild",
            null);
        var warning = new InspectionDiagnostic(
            "DRI1999",
            InspectionDiagnosticSeverity.Warning,
            "Synthetic warning.",
            "test",
            null);

        return InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata("10.0.400", null, "10.0.401"),
            [
                new ProjectInspection(
                    "src/Lib/Lib.csproj",
                    "Lib",
                    "10.0.401",
                    [],
                    ["net10.0"],
                    "Library",
                    false,
                    true,
                    [],
                    new ProjectClassification("library", "high", ["outputType:Library"]),
                    [],
                    [warning]),
                new ProjectInspection(
                    "src/App/App.csproj",
                    "App",
                    "10.0.401",
                    [],
                    ["net10.0"],
                    "Exe",
                    false,
                    false,
                    [],
                    new ProjectClassification("console", "high", ["outputType:Exe"]),
                    [new ProjectReferenceMetadata("src/Lib/Lib.csproj")],
                    [unresolved]),
            ],
            [new InspectionDiagnostic("DRI1998", "info", "Repository fact.", "test", null)]);
    }

    private sealed class StubInspector(InspectionReport report) : IRepositoryInspector
    {
        public static StubInspector Returning(InspectionReport report) => new(report);

        public Task<InspectionReport> InspectAsync(
            RepositoryInspectionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }
}
