using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Git;
using DotNetRepoInspector.MSBuild.Discovery;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.Sdk;

using Xunit;

namespace DotNetRepoInspector.Engine.Tests;

public sealed class InspectionConfigurationTests
{
    [Fact]
    public async Task InspectAsync_AppliesFileExclusionAndTraceableClassificationOverride()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Configuration-").FullName;

        try
        {
            await WriteConfigurationAsync(
                repositoryRoot,
                """
                {
                  "schemaVersion": "1",
                  "exclude": ["Excluded.csproj"],
                  "classificationOverrides": {
                    "Stub.csproj": "web"
                  }
                }
                """);
            var discoverer = new RecordingProjectDiscoverer("Stub.csproj", "Excluded.csproj");
            var evaluator = new RecordingProjectFactsEvaluator();
            var inspector = CreateInspector(discoverer, evaluator);

            var report = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            var project = Assert.Single(report.Projects);
            Assert.Equal("Stub.csproj", project.Path);
            Assert.NotNull(project.Classification);
            Assert.Equal("web", project.Classification.Kind);
            Assert.Null(project.Classification.Confidence);
            Assert.Equal("configuration", project.Classification.Source);
            Assert.Equal("library", project.Classification.AutomaticKind);
            Assert.Equal(1, evaluator.CallCount);
            Assert.Contains("Excluded.csproj", discoverer.ObservedRequest?.ExcludedDirectories ?? []);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_RequestClassificationOverrideTakesPrecedenceOverFile()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Configuration-").FullName;

        try
        {
            await WriteConfigurationAsync(
                repositoryRoot,
                """
                {
                  "schemaVersion": "1",
                  "classificationOverrides": {
                    "Stub.csproj": "web"
                  }
                }
                """);
            var inspector = CreateInspector(
                new RecordingProjectDiscoverer("Stub.csproj"),
                new RecordingProjectFactsEvaluator());
            var request = new RepositoryInspectionRequest(
                repositoryRoot,
                ClassificationOverrides: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Stub.csproj"] = "worker"
                });

            var report = await inspector.InspectAsync(
                request,
                TestContext.Current.CancellationToken);

            var classification = Assert.Single(report.Projects).Classification;
            Assert.NotNull(classification);
            Assert.Equal("worker", classification.Kind);
            Assert.Equal("request", classification.Source);
            Assert.Equal("library", classification.AutomaticKind);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_InvalidConfigurationProducesStableErrorReportWithoutInspecting()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Configuration-").FullName;

        try
        {
            await WriteConfigurationAsync(
                repositoryRoot,
                """
                {
                  "schemaVersion": "2"
                }
                """);
            var discoverer = new RecordingProjectDiscoverer("Stub.csproj");
            var evaluator = new RecordingProjectFactsEvaluator();
            var sdkInspector = new RecordingSdkInspector();
            var gitProvider = new RecordingGitProvider();
            var inspector = new RepositoryInspector(
                discoverer,
                evaluator,
                sdkInspector,
                gitProvider);

            var report = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.Empty(report.Projects);
            var diagnostic = Assert.Single(report.Diagnostics);
            Assert.Equal(InspectionDiagnosticCodes.InvalidConfiguration, diagnostic.Code);
            Assert.Equal(InspectionDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(".dotnetrepoinspector.json", diagnostic.Source);
            Assert.Equal("unsupported-config-schema", diagnostic.Context?["reason"]);
            Assert.Equal(0, discoverer.CallCount);
            Assert.Equal(0, evaluator.CallCount);
            Assert.Equal(0, sdkInspector.CallCount);
            Assert.Equal(0, gitProvider.CallCount);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_NoConfigKeepsZeroConfigBehaviorWhenDefaultFileIsInvalid()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Configuration-").FullName;

        try
        {
            await WriteConfigurationAsync(repositoryRoot, "{ invalid json }");
            var inspector = CreateInspector(
                new RecordingProjectDiscoverer("Stub.csproj"),
                new RecordingProjectFactsEvaluator());

            var report = await inspector.InspectAsync(
                new RepositoryInspectionRequest(
                    repositoryRoot,
                    DisableConfigurationFile: true),
                TestContext.Current.CancellationToken);

            Assert.Single(report.Projects);
            Assert.DoesNotContain(
                report.Diagnostics,
                static diagnostic => diagnostic.Code == InspectionDiagnosticCodes.InvalidConfiguration);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_UnmatchedClassificationOverrideProducesWarning()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Configuration-").FullName;

        try
        {
            await WriteConfigurationAsync(
                repositoryRoot,
                """
                {
                  "schemaVersion": "1",
                  "classificationOverrides": {
                    "Missing.csproj": "web"
                  }
                }
                """);
            var inspector = CreateInspector(
                new RecordingProjectDiscoverer("Stub.csproj"),
                new RecordingProjectFactsEvaluator());

            var report = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            var diagnostic = Assert.Single(
                report.Diagnostics,
                static diagnostic =>
                    diagnostic.Code == InspectionDiagnosticCodes.ClassificationOverrideTargetNotFound);
            Assert.Equal(InspectionDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("Missing.csproj", diagnostic.Source);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static RepositoryInspector CreateInspector(
        RecordingProjectDiscoverer discoverer,
        RecordingProjectFactsEvaluator evaluator) =>
        new(
            discoverer,
            evaluator,
            new RecordingSdkInspector(),
            new RecordingGitProvider());

    private static Task WriteConfigurationAsync(string repositoryRoot, string content) =>
        File.WriteAllTextAsync(
            Path.Combine(repositoryRoot, ".dotnetrepoinspector.json"),
            content,
            TestContext.Current.CancellationToken);

    private sealed class RecordingProjectDiscoverer(params string[] projectPaths) : IProjectDiscoverer
    {
        public int CallCount
        {
            get;
            private set;
        }

        public ProjectDiscoveryRequest? ObservedRequest
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
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            ObservedRequest = request;
            return projectPaths.Select(static path => new DiscoveredProject(path)).ToArray();
        }
    }

    private sealed class RecordingProjectFactsEvaluator : IMsBuildProjectFactsEvaluator
    {
        public int CallCount
        {
            get;
            private set;
        }

        public Task<MsBuildProjectFactsResult> EvaluateAsync(
            string projectPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
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
        public int CallCount
        {
            get;
            private set;
        }

        public Task<DotNetSdkInspectionResult> InspectAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(DotNetSdkInspectionResult.Success(
                repositoryRoot,
                null,
                null,
                "10.0.400"));
        }
    }

    private sealed class RecordingGitProvider : IGitRepositoryMetadataProvider
    {
        public int CallCount
        {
            get;
            private set;
        }

        public Task<GitRepositoryMetadataResult> InspectAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new GitRepositoryMetadataResult(
                new RepositoryMetadata(null, null, null, null, false),
                true,
                path,
                Array.Empty<string>()));
        }
    }
}
