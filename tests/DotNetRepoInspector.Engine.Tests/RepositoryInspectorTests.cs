using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Git;
using DotNetRepoInspector.MSBuild.Discovery;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.Sdk;

using Xunit;

namespace DotNetRepoInspector.Engine.Tests;

public sealed class RepositoryInspectorTests
{
    private static readonly string[] ProjectKindPaths =
    [
        "Console/Console.csproj",
        "Library/Library.csproj",
        "MultiTargeting/MultiTargeting.csproj",
        "Test/Test.csproj",
        "Web/Web.csproj",
        "Worker/Worker.csproj"
    ];

    [Fact]
    public async Task InspectAsync_ProducesCompleteReportForMixedProjectFixture()
    {
        var inspector = new RepositoryInspector();

        var report = await inspector.InspectAsync(
            FixturePath("ProjectKinds"),
            TestContext.Current.CancellationToken);

        Assert.Equal(InspectionSchema.CurrentVersion, report.SchemaVersion);
        Assert.Equal(ProjectKindPaths, report.Projects.Select(static project => project.Path));
        Assert.False(string.IsNullOrWhiteSpace(report.DotNetSdk.ResolvedVersion));
        Assert.Contains(report.Projects, static project => project.Classification?.Kind == "console");
        Assert.Contains(report.Projects, static project => project.Classification?.Kind == "library");
        Assert.Contains(report.Projects, static project => project.Classification?.Kind == "test");
        Assert.Contains(report.Projects, static project => project.Classification?.Kind == "web");
        Assert.Contains(report.Projects, static project => project.Classification?.Kind == "worker");
        Assert.All(
            report.Projects,
            static project => Assert.False(string.IsNullOrWhiteSpace(project.ResolvedSdkVersion)));
    }

    [Fact]
    public async Task InspectAsync_ComposesEvaluatedProjectReferenceGraph()
    {
        var inspector = new RepositoryInspector();

        var report = await inspector.InspectAsync(
            FixturePath("ProjectReferences/Chain"),
            TestContext.Current.CancellationToken);

        var projectA = report.Projects.Single(static project => project.Path == "A/A.csproj");
        var projectB = report.Projects.Single(static project => project.Path == "B/B.csproj");
        var projectC = report.Projects.Single(static project => project.Path == "C/C.csproj");

        Assert.Equal(
            ["B/B.csproj"],
            projectA.References.Select(static reference => reference.Path));
        Assert.Equal(
            ["C/C.csproj"],
            projectB.References.Select(static reference => reference.Path));
        Assert.Empty(projectC.References);
    }

    [Fact]
    public async Task InspectAsync_PreservesSuccessfulProjectsWhenAnotherProjectFails()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Engine-").FullName;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, "Valid.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, "Broken.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>",
                TestContext.Current.CancellationToken);

            var inspector = new RepositoryInspector();
            var report = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.Equal(2, report.Projects.Count);

            var valid = report.Projects.Single(static project => project.Path == "Valid.csproj");
            var broken = report.Projects.Single(static project => project.Path == "Broken.csproj");

            Assert.NotNull(valid.Classification);
            Assert.False(string.IsNullOrWhiteSpace(valid.ResolvedSdkVersion));
            Assert.Empty(valid.Diagnostics);
            Assert.Contains(
                broken.Diagnostics,
                static diagnostic => diagnostic.Severity == InspectionDiagnosticSeverity.Error);

            var health = InspectionHealthEvaluator.Evaluate(report);
            Assert.Equal(1, health.ProjectsWithDiagnostics);
            Assert.Equal(1, health.ProjectsWithErrors);
            Assert.Equal(
                InspectionHealthStatus.Ok,
                InspectionHealthEvaluator.GetProjectStatus(valid));
            Assert.Equal(
                InspectionHealthStatus.Error,
                InspectionHealthEvaluator.GetProjectStatus(broken));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_PropagatesCancellationTokenToLongRunningDependencies()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Engine-").FullName;

        try
        {
            var discoverer = new RecordingProjectDiscoverer();
            var evaluator = new RecordingProjectFactsEvaluator();
            var sdkInspector = new RecordingSdkInspector();
            var gitProvider = new RecordingGitProvider();
            var inspector = new RepositoryInspector(
                discoverer,
                evaluator,
                sdkInspector,
                gitProvider);
            using var cancellationSource = new CancellationTokenSource();
            var token = cancellationSource.Token;

            var report = await inspector.InspectAsync(repositoryRoot, token);

            Assert.Single(report.Projects);
            Assert.Equal(token, discoverer.ObservedToken);
            Assert.Equal(token, evaluator.ObservedToken);
            Assert.Equal(token, sdkInspector.ObservedToken);
            Assert.Equal(token, gitProvider.ObservedToken);
            Assert.Contains(
                report.Diagnostics,
                static diagnostic => diagnostic.Code == InspectionDiagnosticCodes.RepositoryMetadataUnavailable);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_ThrowsWhenCancellationIsAlreadyRequested()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Engine-").FullName;

        try
        {
            var inspector = new RepositoryInspector();
            using var cancellationSource = new CancellationTokenSource();
            await cancellationSource.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => inspector.InspectAsync(repositoryRoot, cancellationSource.Token));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void EngineAssembly_DoesNotDependOnCliOrDeliveryIntegrations()
    {
        var references = typeof(RepositoryInspector)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name)
            .Where(static name => name is not null)
            .ToArray();

        Assert.DoesNotContain("DotNetRepoInspector.Cli", references);
        Assert.DoesNotContain(
            references,
            static name => name!.Contains("GitHub", StringComparison.OrdinalIgnoreCase));
    }

    private static string FixturePath(string relativePath) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed class RecordingProjectDiscoverer : IProjectDiscoverer
    {
        public CancellationToken ObservedToken
        {
            get;
            private set;
        }

        public IReadOnlyList<DiscoveredProject> Discover(ProjectDiscoveryRequest request) =>
            Discover(request, CancellationToken.None);

        public IReadOnlyList<DiscoveredProject> Discover(
            ProjectDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObservedToken = cancellationToken;
            return [new DiscoveredProject("Stub.csproj")];
        }
    }

    private sealed class RecordingProjectFactsEvaluator : IMsBuildProjectFactsEvaluator
    {
        public CancellationToken ObservedToken
        {
            get;
            private set;
        }

        public Task<MsBuildProjectFactsResult> EvaluateAsync(
            string projectPath,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            var facts = new MsBuildProjectFacts(
                "10.0.400",
                [new ProjectSdkReference("Microsoft.NET.Sdk")],
                ["net10.0"],
                "Library",
                false,
                true,
                Array.Empty<string>(),
                new Dictionary<string, string>(StringComparer.Ordinal));

            return Task.FromResult(MsBuildProjectFactsResult.Success(projectPath, facts));
        }
    }

    private sealed class RecordingSdkInspector : IDotNetSdkInspector
    {
        public CancellationToken ObservedToken
        {
            get;
            private set;
        }

        public Task<DotNetSdkInspectionResult> InspectAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            return Task.FromResult(DotNetSdkInspectionResult.Success(
                repositoryRoot,
                null,
                null,
                "10.0.400"));
        }
    }

    private sealed class RecordingGitProvider : IGitRepositoryMetadataProvider
    {
        public CancellationToken ObservedToken
        {
            get;
            private set;
        }

        public Task<GitRepositoryMetadataResult> InspectAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            var metadata = new RepositoryMetadata(
                "sample",
                "0123456789abcdef0123456789abcdef01234567",
                "main",
                null,
                false);
            return Task.FromResult(new GitRepositoryMetadataResult(
                metadata,
                true,
                path,
                ["Git working-tree state could not be determined."]));
        }
    }
}
