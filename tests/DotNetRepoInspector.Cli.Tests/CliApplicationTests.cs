using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_InspectsFixtureAndWritesContractJsonToStdout()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(new RepositoryInspector(), "1.0.0-test");

        var exitCode = await application.RunAsync(
            [FixturePath("ProjectKinds")],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        var report = InspectionJsonSerializer.Deserialize(output.ToString());
        Assert.Equal(InspectionSchema.CurrentVersion, report.SchemaVersion);
        Assert.Equal(6, report.Projects.Count);
        Assert.Contains(report.Projects, static project => project.Classification?.Kind == "web");
        Assert.Contains(report.Projects, static project => project.Classification?.Kind == "worker");
    }

    [Fact]
    public async Task RunAsync_KeepsVerboseLogsOutOfJsonStdout()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var inspector = StubInspector.Returning(CreateReport());
        var application = new CliApplication(inspector, "1.0.0-test");

        var exitCode = await application.RunAsync(
            [".", "--verbose"],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        _ = InspectionJsonSerializer.Deserialize(output.ToString());
        Assert.DoesNotContain("verbose", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[verbose]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WritesReportToFileWhenOutputIsSpecified()
    {
        var temporaryDirectory = Directory.CreateTempSubdirectory("DotNetRepoInspector-Cli-").FullName;

        try
        {
            var outputPath = Path.Combine(temporaryDirectory, "inspection.json");
            using var output = new StringWriter();
            using var error = new StringWriter();
            var application = new CliApplication(
                StubInspector.Returning(CreateReport()),
                "1.0.0-test");

            var exitCode = await application.RunAsync(
                [".", "--output", outputPath],
                output,
                error,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, output.ToString());
            var json = await File.ReadAllTextAsync(
                outputPath,
                TestContext.Current.CancellationToken);
            _ = InspectionJsonSerializer.Deserialize(json);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReturnsInspectionFailureForMissingRepository()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"DotNetRepoInspector-Missing-{Guid.NewGuid():N}");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(new RepositoryInspector(), "1.0.0-test");

        var exitCode = await application.RunAsync(
            [missingPath],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.InspectionFailed, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("[error]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReturnsCompletedWithErrorsAndStillWritesReport()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var report = CreateReport(InspectionDiagnostics.InvalidInspectionRequest());
        var application = new CliApplication(
            StubInspector.Returning(report),
            "1.0.0-test");

        var exitCode = await application.RunAsync(
            ["."],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.CompletedWithErrors, exitCode);
        var deserialized = InspectionJsonSerializer.Deserialize(output.ToString());
        Assert.Contains(
            deserialized.Diagnostics,
            static diagnostic => diagnostic.Severity == InspectionDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RunAsync_ProjectErrorReturnsAggregateFailureWithoutChangingOtherProjectHealth()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var report = CreateReportWithProjectError();
        var application = new CliApplication(
            StubInspector.Returning(report),
            "1.0.0-test");

        var exitCode = await application.RunAsync(
            ["."],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.CompletedWithErrors, exitCode);

        var deserialized = InspectionJsonSerializer.Deserialize(output.ToString());
        Assert.Empty(deserialized.Diagnostics);

        var clean = deserialized.Projects.Single(
            static project => project.Path == "src/Clean/Clean.csproj");
        var broken = deserialized.Projects.Single(
            static project => project.Path == "src/Broken/Broken.csproj");

        Assert.Empty(clean.Diagnostics);
        Assert.Single(broken.Diagnostics);

        var health = InspectionHealthEvaluator.Evaluate(deserialized);
        Assert.Equal(InspectionHealthStatus.Error, health.OverallStatus);
        Assert.Equal(InspectionHealthStatus.Ok, health.RepositoryStatus);
        Assert.Equal(1, health.ProjectsWithDiagnostics);
        Assert.Equal(1, health.ProjectsWithErrors);
        Assert.Equal(
            InspectionHealthStatus.Ok,
            InspectionHealthEvaluator.GetProjectStatus(clean));
        Assert.Equal(
            InspectionHealthStatus.Error,
            InspectionHealthEvaluator.GetProjectStatus(broken));
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidArgumentsWithoutCallingInspector()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var inspector = StubInspector.Returning(CreateReport());
        var application = new CliApplication(inspector, "1.0.0-test");

        var exitCode = await application.RunAsync(
            ["first", "second"],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.InvalidArguments, exitCode);
        Assert.Equal(0, inspector.CallCount);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_HelpDocumentsOptionsAndExamplesWithoutInspecting()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var inspector = StubInspector.Returning(CreateReport());
        var application = new CliApplication(inspector, "1.0.0-test");

        var exitCode = await application.RunAsync(
            ["--help"],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(0, inspector.CallCount);
        Assert.Contains("Usage:", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--output", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Examples:", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_VersionDoesNotInspectRepository()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var inspector = StubInspector.Returning(CreateReport());
        var application = new CliApplication(inspector, "9.8.7-test");

        var exitCode = await application.RunAsync(
            ["--version"],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(0, inspector.CallCount);
        Assert.Equal("9.8.7-test", output.ToString().Trim());
    }

    [Fact]
    public async Task RunAsync_ReturnsCancelledWhenInspectorIsCancelled()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var inspector = new StubInspector(
            static (_, _) => Task.FromException<InspectionReport>(new OperationCanceledException()));
        var application = new CliApplication(inspector, "1.0.0-test");

        var exitCode = await application.RunAsync(
            ["."],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Cancelled, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_ReturnsOutputFailureWhenDestinationCannotBeCreated()
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"DotNetRepoInspector-Missing-{Guid.NewGuid():N}",
            "inspection.json");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = new CliApplication(
            StubInspector.Returning(CreateReport()),
            "1.0.0-test");

        var exitCode = await application.RunAsync(
            [".", "--output", outputPath],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.OutputFailed, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("[error]", error.ToString(), StringComparison.Ordinal);
    }

    private static InspectionReport CreateReport(params InspectionDiagnostic[] diagnostics) =>
        InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, null),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            Array.Empty<ProjectInspection>(),
            diagnostics);

    private static InspectionReport CreateReportWithProjectError()
    {
        var clean = new ProjectInspection(
            "src/Clean/Clean.csproj",
            "Clean",
            "10.0.400",
            [new ProjectSdkMetadata("Microsoft.NET.Sdk", null)],
            ["net10.0"],
            "Library",
            false,
            true,
            Array.Empty<string>(),
            new ProjectClassification("library", "high", ["output-type:Library"]),
            Array.Empty<ProjectReferenceMetadata>(),
            Array.Empty<InspectionDiagnostic>());

        var broken = new ProjectInspection(
            "src/Broken/Broken.csproj",
            "Broken",
            null,
            Array.Empty<ProjectSdkMetadata>(),
            Array.Empty<string>(),
            null,
            null,
            null,
            Array.Empty<string>(),
            null,
            Array.Empty<ProjectReferenceMetadata>(),
            [
                new InspectionDiagnostic(
                    "DRI1006",
                    InspectionDiagnosticSeverity.Error,
                    "MSBuild could not evaluate the project.",
                    "src/Broken/Broken.csproj",
                    null)
            ]);

        return InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, null),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [clean, broken],
            Array.Empty<InspectionDiagnostic>());
    }

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
            ArgumentNullException.ThrowIfNull(handler);
            _handler = handler;
        }

        public int CallCount
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
            return _handler(request, cancellationToken);
        }
    }
}
