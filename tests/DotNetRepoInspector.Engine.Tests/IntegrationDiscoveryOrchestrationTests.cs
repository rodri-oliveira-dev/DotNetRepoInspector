using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Git;
using DotNetRepoInspector.IntegrationDiscovery;
using DotNetRepoInspector.MSBuild.Discovery;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.Sdk;

using Xunit;

namespace DotNetRepoInspector.Engine.Tests;

public sealed class IntegrationDiscoveryOrchestrationTests
{
    [Fact]
    public async Task InspectAsync_DoesNotInvokeIntegrationDiscoveryWithoutOptIn()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Integrations-").FullName;

        try
        {
            var discovery = new RecordingIntegrationDiscoveryPipeline();
            RepositoryInspector inspector = CreateInspector(discovery);

            InspectionReport report = await inspector.InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, discovery.CallCount);
            Assert.Empty(report.Integrations);
            Assert.Equal(IntegrationDiscoveryMetadata.NotExecuted, report.IntegrationDiscovery);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_InvokesIntegrationDiscoveryOnlyAfterProjectsAreAccepted()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Integrations-").FullName;

        try
        {
            IntegrationFinding finding = IntegrationFinding.Create(
                "App/App.csproj",
                IntegrationKind.Http,
                IntegrationDirection.Outbound,
                "httpclient",
                new IntegrationSourceLocation("App/Client.cs", 3),
                IntegrationConfidence.Low,
                ["http:client"]);
            InspectionDiagnostic diagnostic = InspectionDiagnostics.IntegrationDiscoveryFileSkipped(
                "App/Ignored.cs",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["component"] = "integration-discovery",
                    ["reason"] = "read-failed"
                });
            var options = new IntegrationDiscoveryOptions { MaxFindings = 12 };
            var discovery = new RecordingIntegrationDiscoveryPipeline(
                new IntegrationDiscoveryResult([finding], [diagnostic], false));
            RepositoryInspector inspector = CreateInspector(discovery);
            var request = new RepositoryInspectionRequest(
                repositoryRoot,
                ExcludedPaths: ["Excluded"],
                DiscoverIntegrations: true,
                IntegrationDiscoveryOptions: options);

            InspectionReport report = await inspector.InspectAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(1, discovery.CallCount);
            Assert.Equal(TestContext.Current.CancellationToken, discovery.ObservedToken);
            Assert.Equal(options, discovery.ObservedRequest?.Options);
            Assert.Equal(["App/App.csproj"], discovery.ObservedRequest?.Projects.Select(static project => project.ProjectPath));
            Assert.Contains("Excluded", discovery.ObservedRequest?.ExcludedPaths ?? []);
            Assert.Equal(finding, Assert.Single(report.Integrations));
            Assert.Equal(diagnostic, Assert.Single(report.Diagnostics));
            Assert.Equal(new IntegrationDiscoveryMetadata(true, true, false), report.IntegrationDiscovery);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_ConfigurationEnablesDiscoveryAndRequestCanDisableIt()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Integrations-").FullName;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, ".dotnetrepoinspector.json"),
                """
                {
                  "schemaVersion": "2",
                  "integrationDiscovery": { "enabled": true }
                }
                """,
                TestContext.Current.CancellationToken);
            var discovery = new RecordingIntegrationDiscoveryPipeline(
                new IntegrationDiscoveryResult([], [], true));
            RepositoryInspector inspector = CreateInspector(discovery);

            InspectionReport enabled = await inspector.InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);
            InspectionReport disabled = await inspector.InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot, DiscoverIntegrations: false),
                TestContext.Current.CancellationToken);

            Assert.Equal(new IntegrationDiscoveryMetadata(true, true, true), enabled.IntegrationDiscovery);
            Assert.Equal(IntegrationDiscoveryMetadata.NotExecuted, disabled.IntegrationDiscovery);
            Assert.Equal(1, discovery.CallCount);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_RejectsIntegrationDiscoveryConfigurationWithoutEnabled()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Integrations-").FullName;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, ".dotnetrepoinspector.json"),
                """
                {
                  "schemaVersion": "2",
                  "integrationDiscovery": {}
                }
                """,
                TestContext.Current.CancellationToken);
            var discovery = new RecordingIntegrationDiscoveryPipeline();

            InspectionReport report = await CreateInspector(discovery).InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, discovery.CallCount);
            Assert.Equal(
                "integration-discovery-enabled-required",
                Assert.Single(report.Diagnostics).Context?["reason"]);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static RepositoryInspector CreateInspector(
        IIntegrationDiscoveryPipeline integrationDiscoveryPipeline) =>
        new(
            new StubProjectDiscoverer(),
            new StubProjectFactsEvaluator(),
            new StubSdkInspector(),
            new StubGitProvider(),
            integrationDiscoveryPipeline);

    private sealed class RecordingIntegrationDiscoveryPipeline : IIntegrationDiscoveryPipeline
    {
        private readonly IntegrationDiscoveryResult _result;

        public RecordingIntegrationDiscoveryPipeline(IntegrationDiscoveryResult? result = null)
        {
            _result = result ?? IntegrationDiscoveryResult.Empty;
        }

        public int CallCount
        {
            get; private set;
        }

        public IntegrationDiscoveryRequest? ObservedRequest
        {
            get; private set;
        }

        public CancellationToken ObservedToken
        {
            get; private set;
        }

        public Task<IntegrationDiscoveryResult> DiscoverAsync(
            IntegrationDiscoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ObservedRequest = request;
            ObservedToken = cancellationToken;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubProjectDiscoverer : IProjectDiscoverer
    {
        public IReadOnlyList<DiscoveredProject> Discover(ProjectDiscoveryRequest request) =>
            [new("App/App.csproj")];

        public IReadOnlyList<DiscoveredProject> Discover(
            ProjectDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Discover(request);
        }
    }

    private sealed class StubProjectFactsEvaluator : IMsBuildProjectFactsEvaluator
    {
        public Task<MsBuildProjectFactsResult> EvaluateAsync(
            string projectPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private sealed class StubSdkInspector : IDotNetSdkInspector
    {
        public Task<DotNetSdkInspectionResult> InspectAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DotNetSdkInspectionResult.Success(
                repositoryRoot,
                null,
                null,
                "10.0.400"));
        }
    }

    private sealed class StubGitProvider : IGitRepositoryMetadataProvider
    {
        public Task<GitRepositoryMetadataResult> InspectAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GitRepositoryMetadataResult(
                new RepositoryMetadata("fixture", null, null, null, false),
                true,
                path,
                Array.Empty<string>()));
        }
    }
}
