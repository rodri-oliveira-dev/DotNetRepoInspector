using DotNetRepoInspector.ContainerDiscovery;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Git;
using DotNetRepoInspector.IntegrationDiscovery;
using DotNetRepoInspector.MSBuild.Discovery;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.Sdk;

using Xunit;

namespace DotNetRepoInspector.Engine.Tests;

public sealed class ContainerDiscoveryOrchestrationTests
{
    [Fact]
    public async Task InspectAsync_DoesNotInvokeContainerDiscoveryWithoutOptIn()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Containers-").FullName;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, "Dockerfile"),
                "FROM private.example/image:secret-tag",
                TestContext.Current.CancellationToken);
            var discovery = new RecordingContainerDiscoveryPipeline();

            InspectionReport report = await CreateInspector(discovery).InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, discovery.CallCount);
            Assert.Empty(report.ContainerImages);
            Assert.Equal(ContainerDiscoveryMetadata.NotExecuted, report.ContainerDiscovery);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_MapsContainerEvidenceDiagnosticsAndPartialState()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Containers-").FullName;

        try
        {
            var evidence = new DockerImageEvidence(
                "deploy/Dockerfile",
                4,
                "mcr.microsoft.com/dotnet/aspnet:10.0",
                "mcr.microsoft.com",
                "dotnet/aspnet",
                "10.0",
                null,
                "runtime",
                DockerImageReferenceKind.External,
                DockerImageResolutionStatus.Resolved,
                DotNetContainerImageFamily.AspNet,
                true);
            var diagnostic = new ContainerDiscoveryDiagnostic(
                ContainerDiscoveryDiagnosticCodes.LimitReached,
                "results",
                "deploy/Dockerfile");
            var options = new ContainerDiscoveryOptions { MaxResults = 12 };
            var discovery = new RecordingContainerDiscoveryPipeline(
                new ContainerDiscoveryResult([evidence], [diagnostic], true));

            InspectionReport report = await CreateInspector(discovery).InspectAsync(
                new RepositoryInspectionRequest(
                    repositoryRoot,
                    ExcludedPaths: ["Excluded"],
                    DiscoverContainers: true,
                    ContainerDiscoveryOptions: options),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, discovery.CallCount);
            Assert.Equal(options, discovery.ObservedRequest?.Options);
            Assert.Contains("Excluded", discovery.ObservedRequest?.ExcludedPaths ?? []);
            ContainerImageEvidence image = Assert.Single(report.ContainerImages);
            Assert.Equal("deploy/Dockerfile", image.Source.Path);
            Assert.Equal(ContainerDotNetImageFamily.AspNet, image.DotNetFamily);
            Assert.True(image.IsOfficialDotNetImage);
            InspectionDiagnostic mappedDiagnostic = Assert.Single(report.Diagnostics);
            Assert.Equal(InspectionDiagnosticCodes.ContainerDiscoveryLimitReached, mappedDiagnostic.Code);
            Assert.Equal("results", mappedDiagnostic.Context?["reason"]);
            Assert.Equal(ContainerDiscoveryMetadata.Complete(true), report.ContainerDiscovery);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_ConfigurationEnablesDiscoveryAndRequestCanDisableIt()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Containers-").FullName;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, ".dotnetrepoinspector.json"),
                """
                {
                  "schemaVersion": "2",
                  "containerDiscovery": { "enabled": true }
                }
                """,
                TestContext.Current.CancellationToken);
            var discovery = new RecordingContainerDiscoveryPipeline();
            RepositoryInspector inspector = CreateInspector(discovery);

            InspectionReport enabled = await inspector.InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);
            InspectionReport disabled = await inspector.InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot, DiscoverContainers: false),
                TestContext.Current.CancellationToken);

            Assert.Equal(ContainerDiscoveryMetadata.Complete(false), enabled.ContainerDiscovery);
            Assert.Equal(ContainerDiscoveryMetadata.NotExecuted, disabled.ContainerDiscovery);
            Assert.Equal(1, discovery.CallCount);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_RejectsContainerDiscoveryConfigurationWithoutEnabled()
    {
        string repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Containers-").FullName;

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repositoryRoot, ".dotnetrepoinspector.json"),
                """
                {
                  "schemaVersion": "2",
                  "containerDiscovery": {}
                }
                """,
                TestContext.Current.CancellationToken);
            var discovery = new RecordingContainerDiscoveryPipeline();

            InspectionReport report = await CreateInspector(discovery).InspectAsync(
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, discovery.CallCount);
            Assert.Equal(
                "container-discovery-enabled-required",
                Assert.Single(report.Diagnostics).Context?["reason"]);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static RepositoryInspector CreateInspector(
        IContainerDiscoveryPipeline containerDiscoveryPipeline) =>
        new(
            new EmptyProjectDiscoverer(),
            new UnusedProjectFactsEvaluator(),
            new StubSdkInspector(),
            new StubGitProvider(),
            new EmptyIntegrationDiscoveryPipeline(),
            containerDiscoveryPipeline);

    private sealed class RecordingContainerDiscoveryPipeline : IContainerDiscoveryPipeline
    {
        private readonly ContainerDiscoveryResult _result;

        public RecordingContainerDiscoveryPipeline(ContainerDiscoveryResult? result = null)
        {
            _result = result ?? ContainerDiscoveryResult.Empty;
        }

        public int CallCount
        {
            get; private set;
        }

        public ContainerDiscoveryRequest? ObservedRequest
        {
            get; private set;
        }

        public Task<ContainerDiscoveryResult> DiscoverAsync(
            ContainerDiscoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            ObservedRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class EmptyIntegrationDiscoveryPipeline : IIntegrationDiscoveryPipeline
    {
        public Task<IntegrationDiscoveryResult> DiscoverAsync(
            IntegrationDiscoveryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(IntegrationDiscoveryResult.Empty);
    }

    private sealed class EmptyProjectDiscoverer : IProjectDiscoverer
    {
        public IReadOnlyList<DiscoveredProject> Discover(ProjectDiscoveryRequest request) => [];

        public IReadOnlyList<DiscoveredProject> Discover(
            ProjectDiscoveryRequest request,
            CancellationToken cancellationToken) => [];
    }

    private sealed class UnusedProjectFactsEvaluator : IMsBuildProjectFactsEvaluator
    {
        public Task<MsBuildProjectFactsResult> EvaluateAsync(
            string projectPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No projects should be evaluated by this fixture.");
    }

    private sealed class StubSdkInspector : IDotNetSdkInspector
    {
        public Task<DotNetSdkInspectionResult> InspectAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DotNetSdkInspectionResult.Success(
                repositoryRoot,
                null,
                null,
                "10.0.400"));
    }

    private sealed class StubGitProvider : IGitRepositoryMetadataProvider
    {
        public Task<GitRepositoryMetadataResult> InspectAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitRepositoryMetadataResult(
                new RepositoryMetadata("fixture", null, null, null, false),
                true,
                path,
                Array.Empty<string>()));
    }
}
