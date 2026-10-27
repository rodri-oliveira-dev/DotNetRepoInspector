using Xunit;

namespace DotNetRepoInspector.ContainerDiscovery.Tests;

public sealed class ContainerDiscoveryPipelineTests
{
    [Fact]
    public void Options_DefineExplicitPositiveDefaultBudgets()
    {
        var options = new ContainerDiscoveryOptions();

        Assert.Equal(10_000, options.MaxVisitedPaths);
        Assert.Equal(500, options.MaxDockerfiles);
        Assert.Equal(1_048_576, options.MaxBytesPerFile);
        Assert.Equal(20_971_520, options.MaxTotalBytes);
        Assert.Equal(32, options.MaxDepth);
        Assert.Equal(1_000, options.MaxResults);
        Assert.Equal(100, options.MaxDiagnostics);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxDuration);
    }

    [Fact]
    public async Task DiscoverAsync_ParsesTagsDigestPlatformContinuationAndLocations()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            Request(exclusions: ExcludeAllExcept("Simple")),
            TestContext.Current.CancellationToken);

        Assert.False(result.Truncated);
        Assert.Empty(result.Diagnostics);
        Assert.Collection(
            result.Images,
            image =>
            {
                Assert.Equal("Simple/Dockerfile", image.SourcePath);
                Assert.Equal(2, image.Line);
                Assert.Equal("ubuntu:latest", image.ImageReference);
                Assert.Null(image.Registry);
                Assert.Equal("ubuntu", image.Repository);
                Assert.Equal("latest", image.Tag);
                Assert.Null(image.Digest);
                Assert.Equal(DockerImageReferenceKind.External, image.ReferenceKind);
                Assert.False(image.IsOfficialDotNetImage);
            },
            image =>
            {
                Assert.Equal(3, image.Line);
                Assert.Equal("alpine:3.20@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", image.ImageReference);
                Assert.Equal("alpine", image.Repository);
                Assert.Equal("3.20", image.Tag);
                Assert.Equal("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", image.Digest);
            },
            image =>
            {
                Assert.Equal(5, image.Line);
                Assert.Equal("mcr.microsoft.com", image.Registry);
                Assert.Equal("dotnet/sdk", image.Repository);
                Assert.Equal("10.0", image.Tag);
                Assert.Equal("build", image.StageAlias);
                Assert.Equal(DotNetContainerImageFamily.Sdk, image.DotNetFamily);
                Assert.True(image.IsOfficialDotNetImage);
            });
    }

    [Fact]
    public async Task DiscoverAsync_DistinguishesExternalStageAndScratchReferences()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            Request(exclusions: ExcludeAllExcept("MultiStage")),
            TestContext.Current.CancellationToken);

        Assert.Collection(
            result.Images,
            image =>
            {
                Assert.Equal(DockerImageReferenceKind.External, image.ReferenceKind);
                Assert.Equal("Build", image.StageAlias);
                Assert.Equal(DotNetContainerImageFamily.Sdk, image.DotNetFamily);
            },
            image =>
            {
                Assert.Equal(DockerImageReferenceKind.Stage, image.ReferenceKind);
                Assert.Equal("build", image.ImageReference);
                Assert.Equal("publish", image.StageAlias);
                Assert.Null(image.Repository);
            },
            image =>
            {
                Assert.Equal(DockerImageReferenceKind.Scratch, image.ReferenceKind);
                Assert.Equal("scratch", image.ImageReference);
                Assert.Equal("final", image.StageAlias);
            });
    }

    [Fact]
    public async Task DiscoverAsync_ResolvesOnlyKnownGlobalArgumentDefaults()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            Request(exclusions: ExcludeAllExcept("Arguments")),
            TestContext.Current.CancellationToken);

        Assert.Collection(
            result.Images,
            image =>
            {
                Assert.Equal(DockerImageResolutionStatus.Resolved, image.ResolutionStatus);
                Assert.Equal("mcr.microsoft.com/dotnet/aspnet:10.0", image.ImageReference);
                Assert.Equal(DotNetContainerImageFamily.AspNet, image.DotNetFamily);
                Assert.True(image.IsOfficialDotNetImage);
            },
            image =>
            {
                Assert.Equal(DockerImageResolutionStatus.Unresolved, image.ResolutionStatus);
                Assert.Null(image.ImageReference);
                Assert.Equal("unknown", image.StageAlias);
            },
            image =>
            {
                Assert.Equal(DockerImageResolutionStatus.Unresolved, image.ResolutionStatus);
                Assert.Null(image.ImageReference);
            },
            image =>
            {
                Assert.Equal(DockerImageResolutionStatus.Resolved, image.ResolutionStatus);
                Assert.Equal("registry.example/dotnet/sdk:10.0", image.ImageReference);
                Assert.Null(image.DotNetFamily);
                Assert.False(image.IsOfficialDotNetImage);
            });
    }

    [Fact]
    public async Task DiscoverAsync_RecognizesOnlyOfficialDotNetFamiliesAtMcr()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            Request(exclusions: ExcludeAllExcept("Families")),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                DotNetContainerImageFamily.Sdk,
                DotNetContainerImageFamily.AspNet,
                DotNetContainerImageFamily.Runtime,
                DotNetContainerImageFamily.RuntimeDeps
            ],
            result.Images.Take(4).Select(static image => image.DotNetFamily));
        Assert.All(result.Images.Take(4), static image => Assert.True(image.IsOfficialDotNetImage));
        Assert.Null(result.Images[4].DotNetFamily);
        Assert.False(result.Images[4].IsOfficialDotNetImage);
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsUnresolvedEvidenceAndControlledMalformedDiagnostics()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            Request(exclusions: ExcludeAllExcept("Malformed")),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Images);
        Assert.All(
            result.Images,
            static image => Assert.Equal(
                DockerImageResolutionStatus.Unresolved,
                image.ResolutionStatus));
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code == ContainerDiscoveryDiagnosticCodes.DockerfileParseFailed &&
                diagnostic.Reason == "malformed-from");
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Reason.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotExpandSecretShapedArgumentDefaults()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            Request(exclusions: ExcludeAllExcept("SensitiveArguments")),
            TestContext.Current.CancellationToken);

        DockerImageEvidence image = Assert.Single(result.Images);
        Assert.Equal(DockerImageResolutionStatus.Unresolved, image.ResolutionStatus);
        Assert.Null(image.ImageReference);
        Assert.DoesNotContain(
            "fixture-secret",
            string.Join('|', result.Images.Select(static item => item.ImageReference)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_EnforcesFileBytePathDepthAndResultLimits()
    {
        var pipeline = new ContainerDiscoveryPipeline();
        (ContainerDiscoveryOptions Options, string Reason)[] cases =
        [
            (new ContainerDiscoveryOptions { MaxBytesPerFile = 32 }, "file-size-limit"),
            (new ContainerDiscoveryOptions { MaxTotalBytes = 1 }, "total-bytes"),
            (new ContainerDiscoveryOptions { MaxVisitedPaths = 1 }, "visited-paths"),
            (new ContainerDiscoveryOptions { MaxDepth = 0 }, "depth"),
            (new ContainerDiscoveryOptions { MaxResults = 1 }, "results")
        ];

        foreach ((ContainerDiscoveryOptions options, string reason) in cases)
        {
            ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
                Request(options: options),
                TestContext.Current.CancellationToken);

            Assert.True(result.Truncated);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Reason == reason);
        }
    }

    [Fact]
    public async Task DiscoverAsync_EnforcesDockerfileAndDiagnosticLimits()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult fileLimited = await pipeline.DiscoverAsync(
            Request(options: new ContainerDiscoveryOptions { MaxDockerfiles = 1 }),
            TestContext.Current.CancellationToken);
        ContainerDiscoveryResult diagnosticLimited = await pipeline.DiscoverAsync(
            Request(options: new ContainerDiscoveryOptions
            {
                MaxBytesPerFile = 16,
                MaxDiagnostics = 1
            }),
            TestContext.Current.CancellationToken);

        Assert.True(fileLimited.Truncated);
        Assert.Contains(fileLimited.Diagnostics, static diagnostic => diagnostic.Reason == "dockerfiles");
        Assert.True(diagnosticLimited.Truncated);
        ContainerDiscoveryDiagnostic diagnostic = Assert.Single(diagnosticLimited.Diagnostics);
        Assert.Equal(ContainerDiscoveryDiagnosticCodes.LimitReached, diagnostic.Code);
        Assert.Equal("diagnostics", diagnostic.Reason);
    }

    [Fact]
    public async Task DiscoverAsync_MarksExactResultLimitAsTruncatedWhenCandidatesRemain()
    {
        using var repository = new TemporaryRepository();
        await repository.WriteTextAsync("Dockerfile.a", "FROM alpine:3.20\n");
        await repository.WriteTextAsync("Dockerfile.b", "FROM ubuntu:24.04\n");
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            new ContainerDiscoveryRequest(
                repository.Path,
                Options: new ContainerDiscoveryOptions { MaxResults = 1 }),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Images);
        Assert.True(result.Truncated);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Reason == "results");
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotMarkExactResultLimitAsTruncatedWhenScanIsComplete()
    {
        using var repository = new TemporaryRepository();
        await repository.WriteTextAsync("Dockerfile", "FROM alpine:3.20\n");
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            new ContainerDiscoveryRequest(
                repository.Path,
                Options: new ContainerDiscoveryOptions { MaxResults = 1 }),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Images);
        Assert.False(result.Truncated);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Reason == "results");
    }

    [Fact]
    public async Task DiscoverAsync_AppliesVisitedPathBudgetWhileCollectingDirectoryEntries()
    {
        using var repository = new TemporaryRepository();
        for (int index = 0; index < 20; index++)
        {
            await repository.WriteTextAsync($"entry-{index:D2}.txt", string.Empty);
        }

        var pipeline = new ContainerDiscoveryPipeline();
        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            new ContainerDiscoveryRequest(
                repository.Path,
                Options: new ContainerDiscoveryOptions { MaxVisitedPaths = 2 }),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Images);
        Assert.True(result.Truncated);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Reason == "visited-paths");
    }

    [Fact]
    public async Task DiscoverAsync_ChargesInvalidUtf8FilesAgainstTotalByteBudget()
    {
        using var repository = new TemporaryRepository();
        await repository.WriteBytesAsync("Dockerfile.a", [0xff, 0xff]);
        await repository.WriteBytesAsync("Dockerfile.b", [0xff, 0xff]);
        var pipeline = new ContainerDiscoveryPipeline();

        ContainerDiscoveryResult result = await pipeline.DiscoverAsync(
            new ContainerDiscoveryRequest(
                repository.Path,
                Options: new ContainerDiscoveryOptions { MaxTotalBytes = 3 }),
            TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Reason == "invalid-utf8");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Reason == "total-bytes");
    }

    [Fact]
    public async Task DiscoverAsync_RejectsEscapingExclusionsAndPropagatesCallerCancellation()
    {
        var pipeline = new ContainerDiscoveryPipeline();

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.DiscoverAsync(
            Request(exclusions: ["../outside"]),
            TestContext.Current.CancellationToken));

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.DiscoverAsync(
            Request(),
            cancellationSource.Token));
    }

    [Fact]
    public async Task DiscoverAsync_IsDeterministicAndHonorsExclusions()
    {
        var pipeline = new ContainerDiscoveryPipeline();
        ContainerDiscoveryRequest request = Request(exclusions: ["Malformed", "Large"]);

        ContainerDiscoveryResult first = await pipeline.DiscoverAsync(
            request,
            TestContext.Current.CancellationToken);
        ContainerDiscoveryResult second = await pipeline.DiscoverAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(first.Images, second.Images);
        Assert.DoesNotContain(first.Images, static image => image.SourcePath.StartsWith("Malformed/", StringComparison.Ordinal));
        Assert.DoesNotContain(first.Images, static image => image.SourcePath.StartsWith("Large/", StringComparison.Ordinal));
    }

    private static ContainerDiscoveryRequest Request(
        IReadOnlyList<string>? exclusions = null,
        ContainerDiscoveryOptions? options = null) =>
        new(FixtureRoot, exclusions, options);

    private static string[] ExcludeAllExcept(string included) =>
        Directory.EnumerateDirectories(FixtureRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, included, StringComparison.Ordinal))
            .Select(static name => name!)
            .ToArray();

    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ContainerDiscovery");

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-repo-inspector-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path
        {
            get;
        }

        public Task WriteTextAsync(string relativePath, string content) =>
            File.WriteAllTextAsync(
                System.IO.Path.Combine(Path, relativePath),
                content,
                TestContext.Current.CancellationToken);

        public Task WriteBytesAsync(string relativePath, byte[] content) =>
            File.WriteAllBytesAsync(
                System.IO.Path.Combine(Path, relativePath),
                content,
                TestContext.Current.CancellationToken);

        public void Dispose() =>
            Directory.Delete(Path, recursive: true);
    }
}
