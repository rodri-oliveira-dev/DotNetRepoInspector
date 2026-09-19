using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Mcp.Tests;

public sealed class InspectRepositoryHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_UsesConfiguredRootAndPreservesCanonicalReport()
    {
        var report = CreateReport();
        var inspector = StubInspector.Returning(report);
        var root = new RepositoryRoot(Path.GetFullPath("."));
        var handler = new InspectRepositoryHandler(inspector, root);

        var result = await handler.ExecuteAsync(
            "config/settings.json",
            disableConfigurationFile: false,
            ["src/App/App.csproj"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/App/App.csproj"] = "web"
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.NotNull(result.StructuredContent);
        var structuredContent = result.StructuredContent.Value;
        Assert.True(structuredContent.GetProperty("ok").GetBoolean());
        var actualReport = structuredContent
            .GetProperty("data")
            .GetProperty("report")
            .GetRawText();
        Assert.Equal(
            InspectionJsonSerializer.Serialize(report),
            InspectionJsonSerializer.Serialize(InspectionJsonSerializer.Deserialize(actualReport)));

        Assert.NotNull(inspector.Request);
        Assert.Equal(root.FullPath, inspector.Request.RepositoryRoot);
        Assert.Equal("config/settings.json", inspector.Request.ConfigurationPath);
        Assert.Equal(["src/App/App.csproj"], inspector.Request.ExcludedPaths);
        Assert.Equal("web", inspector.Request.ClassificationOverrides!["src/App/App.csproj"]);
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("../../outside.json")]
    public async Task ExecuteAsync_RejectsPathsOutsideConfiguredRoot(string configurationPath)
    {
        var inspector = StubInspector.Returning(CreateReport());
        var handler = new InspectRepositoryHandler(
            inspector,
            new RepositoryRoot(Path.GetFullPath(".")));

        var result = await handler.ExecuteAsync(
            configurationPath,
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(0, inspector.CallCount);
        Assert.Equal(
            "path_outside_repository_root",
            result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("code")
            .GetString());
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAbsolutePathsOutsideConfiguredRoot()
    {
        var inspector = StubInspector.Returning(CreateReport());
        var handler = new InspectRepositoryHandler(
            inspector,
            new RepositoryRoot(Path.GetFullPath(".")));

        var result = await handler.ExecuteAsync(
            Path.GetFullPath("outside.json"),
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(0, inspector.CallCount);
        Assert.Equal(
            "path_outside_repository_root",
            result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task ExecuteAsync_RejectsExcludedPathOutsideConfiguredRoot()
    {
        var inspector = StubInspector.Returning(CreateReport());
        var handler = new InspectRepositoryHandler(
            inspector,
            new RepositoryRoot(Path.GetFullPath(".")));

        var result = await handler.ExecuteAsync(
            null,
            disableConfigurationFile: false,
            excludedPaths: ["../Outside.csproj"],
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(0, inspector.CallCount);
        Assert.Equal(
            "path_outside_repository_root",
            result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task ExecuteAsync_RejectsClassificationOverrideOutsideConfiguredRoot()
    {
        var inspector = StubInspector.Returning(CreateReport());
        var handler = new InspectRepositoryHandler(
            inspector,
            new RepositoryRoot(Path.GetFullPath(".")));

        var result = await handler.ExecuteAsync(
            null,
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["../Outside.csproj"] = "web"
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(0, inspector.CallCount);
        Assert.Equal(
            "path_outside_repository_root",
            result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }

    [Fact]
    public async Task ExecuteAsync_MapsExpectedFailuresWithoutLeakingExceptionDetails()
    {
        const string secret = "super-secret-token";
        var inspector = new StubInspector(
            static (_, _) => throw new IOException(secret));
        var handler = new InspectRepositoryHandler(
            inspector,
            new RepositoryRoot(Path.GetFullPath(".")));

        var result = await handler.ExecuteAsync(
            null,
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var json = result.StructuredContent!.Value.GetRawText();
        Assert.Contains("inspection_failed", json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationToEngineAndCaller()
    {
        CancellationToken observedToken = default;
        var inspector = new StubInspector((_, cancellationToken) =>
        {
            observedToken = cancellationToken;
            return Task.FromCanceled<InspectionReport>(cancellationToken);
        });
        var handler = new InspectRepositoryHandler(
            inspector,
            new RepositoryRoot(Path.GetFullPath(".")));
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.ExecuteAsync(
                null,
                disableConfigurationFile: false,
                excludedPaths: null,
                classificationOverrides: null,
                cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, observedToken);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsInvalidInputForConflictingConfigurationOptions()
    {
        var inspector = StubInspector.Returning(CreateReport());
        var handler = new InspectRepositoryHandler(
            inspector,
            new RepositoryRoot(Path.GetFullPath(".")));

        var result = await handler.ExecuteAsync(
            "config.json",
            disableConfigurationFile: true,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(0, inspector.CallCount);
        Assert.Equal(
            "invalid_tool_input",
            result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("code")
            .GetString());
    }

    [Fact]
    public async Task ExecuteAsync_PreservesMissingSdkAsEngineDiagnostics()
    {
        var handler = new InspectRepositoryHandler(
            new RepositoryInspector(),
            new RepositoryRoot(FixturePath("Compatibility/MissingSdk")));

        var result = await handler.ExecuteAsync(
            null,
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        var reportJson = result.StructuredContent!.Value
            .GetProperty("data")
            .GetProperty("report")
            .GetRawText();
        var report = InspectionJsonSerializer.Deserialize(reportJson);
        Assert.Contains(
            report.Diagnostics,
            static diagnostic => diagnostic.Severity == InspectionDiagnosticSeverity.Error);
    }

    private static InspectionReport CreateReport() =>
        InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata("10.0.400", null, "10.0.401"),
            Array.Empty<ProjectInspection>(),
            Array.Empty<InspectionDiagnostic>());

    private static string FixturePath(string relativePath) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed class StubInspector : IRepositoryInspector
    {
        private readonly Func<RepositoryInspectionRequest, CancellationToken, Task<InspectionReport>> _handler;

        public StubInspector(
            Func<RepositoryInspectionRequest, CancellationToken, Task<InspectionReport>> handler)
        {
            _handler = handler;
        }

        public int CallCount
        {
            get;
            private set;
        }

        public RepositoryInspectionRequest? Request
        {
            get;
            private set;
        }

        public static StubInspector Returning(InspectionReport report) =>
            new((_, _) => Task.FromResult(report));

        public Task<InspectionReport> InspectAsync(
            RepositoryInspectionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            return _handler(request, cancellationToken);
        }
    }
}
