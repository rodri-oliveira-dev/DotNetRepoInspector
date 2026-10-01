using DotNetRepoInspector.Core.Contracts;

using Microsoft.CodeAnalysis.CSharp.Syntax;

using Xunit;

namespace DotNetRepoInspector.IntegrationDiscovery.Tests;

public sealed class IntegrationDiscoveryPipelineTests
{
    [Fact]
    public void Options_DefineExplicitPositiveDefaultBudgets()
    {
        var options = new IntegrationDiscoveryOptions();

        Assert.Equal(10_000, options.MaxVisitedPaths);
        Assert.Equal(2_000, options.MaxSourceFiles);
        Assert.Equal(1_048_576, options.MaxBytesPerFile);
        Assert.Equal(20_971_520, options.MaxTotalBytes);
        Assert.Equal(1_000, options.MaxFindings);
        Assert.Equal(100, options.MaxDiagnostics);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxDuration);
    }

    [Fact]
    public void Constructor_RejectsUnsafeOrDuplicateDetectorIds()
    {
        Assert.Throws<ArgumentException>(() => new IntegrationDiscoveryPipeline(
        [
            new ConfigurationKeyDetector("duplicate", []),
            new ConfigurationKeyDetector("duplicate", [])
        ]));
        Assert.Throws<ArgumentException>(() => new IntegrationDiscoveryPipeline(
            [new ConfigurationKeyDetector("unsafe\nid", [])]));
    }

    [Fact]
    public async Task DiscoverAsync_MapsFilesToProjectsAndDeduplicatesFindingsDeterministically()
    {
        var invocationOrder = new List<string>();
        var pipeline = new IntegrationDiscoveryPipeline(
        [
            new ConfigurationKeyDetector("zeta", invocationOrder),
            new ConfigurationKeyDetector("alpha", invocationOrder)
        ]);
        var request = Request(
            "TwinB/TwinB.csproj",
            "TwinA/TwinA.csproj");

        IntegrationDiscoveryResult first = await pipeline.DiscoverAsync(
            request,
            TestContext.Current.CancellationToken);
        IntegrationDiscoveryResult second = await pipeline.DiscoverAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["TwinA/TwinA.csproj", "TwinB/TwinB.csproj"],
            first.Findings.Select(static finding => finding.ProjectPath));
        Assert.Equal(
            ["Messaging:TwinA", "Messaging:TwinB"],
            first.Findings.Select(static finding => finding.ConfigurationKey));
        Assert.Equal(
            first.Findings.Select(static finding => finding.Id),
            second.Findings.Select(static finding => finding.Id));
        Assert.Equal(["alpha", "zeta", "alpha", "zeta"], invocationOrder.Take(4));
        Assert.Empty(first.Diagnostics);
        Assert.False(first.Truncated);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsInvalidSyntaxWithoutReturningSource()
    {
        var pipeline = new IntegrationDiscoveryPipeline(
            [new ConfigurationKeyDetector("configuration", [])]);

        IntegrationDiscoveryResult result = await pipeline.DiscoverAsync(
            Request("Invalid/Invalid.csproj"),
            TestContext.Current.CancellationToken);

        InspectionDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(InspectionDiagnosticCodes.IntegrationDiscoveryParseFailed, diagnostic.Code);
        Assert.Equal("Invalid/Broken.cs", diagnostic.Source);
        Assert.DoesNotContain("internal sealed", InspectionJsonSerializer.Serialize(Report(result)));
    }

    [Fact]
    public async Task DiscoverAsync_EnforcesPerFileAndFindingBudgets()
    {
        var pipeline = new IntegrationDiscoveryPipeline(
            [new ConfigurationKeyDetector("configuration", [])]);
        var smallFileBudget = new IntegrationDiscoveryOptions
        {
            MaxBytesPerFile = 32
        };

        IntegrationDiscoveryResult fileLimited = await pipeline.DiscoverAsync(
            RequestWithOptions(smallFileBudget, "Large/Large.csproj"),
            TestContext.Current.CancellationToken);
        IntegrationDiscoveryResult findingLimited = await pipeline.DiscoverAsync(
            RequestWithOptions(
                new IntegrationDiscoveryOptions { MaxFindings = 1 },
                "TwinA/TwinA.csproj",
                "TwinB/TwinB.csproj"),
            TestContext.Current.CancellationToken);

        Assert.Empty(fileLimited.Findings);
        Assert.Contains(
            fileLimited.Diagnostics,
            static diagnostic =>
                diagnostic.Code == InspectionDiagnosticCodes.IntegrationDiscoveryFileSkipped &&
                diagnostic.Context!["reason"] == "file-size-limit");
        Assert.Single(findingLimited.Findings);
        Assert.True(findingLimited.Truncated);
        Assert.Contains(
            findingLimited.Diagnostics,
            static diagnostic =>
                diagnostic.Code == InspectionDiagnosticCodes.IntegrationDiscoveryLimitReached &&
                diagnostic.Context!["reason"] == "findings");
    }

    [Fact]
    public async Task DiscoverAsync_EnforcesPathFileAndTotalByteBudgets()
    {
        var pipeline = new IntegrationDiscoveryPipeline(
            [new ConfigurationKeyDetector("configuration", [])]);
        (IntegrationDiscoveryOptions Options, string Reason)[] cases =
        [
            (new IntegrationDiscoveryOptions { MaxVisitedPaths = 1 }, "visited-paths"),
            (new IntegrationDiscoveryOptions { MaxSourceFiles = 1 }, "source-files"),
            (new IntegrationDiscoveryOptions { MaxTotalBytes = 1 }, "total-bytes")
        ];

        foreach ((IntegrationDiscoveryOptions options, string reason) in cases)
        {
            IntegrationDiscoveryResult result = await pipeline.DiscoverAsync(
                RequestWithOptions(
                    options,
                    "TwinA/TwinA.csproj",
                    "TwinB/TwinB.csproj"),
                TestContext.Current.CancellationToken);

            Assert.True(result.Truncated);
            Assert.Contains(
                result.Diagnostics,
                diagnostic =>
                    diagnostic.Code == InspectionDiagnosticCodes.IntegrationDiscoveryLimitReached &&
                    diagnostic.Context!["reason"] == reason);
        }
    }

    [Fact]
    public async Task DiscoverAsync_IgnoresGeneratedAndExcludedFiles()
    {
        var pipeline = new IntegrationDiscoveryPipeline(
            [new ConfigurationKeyDetector("configuration", [])]);

        IntegrationDiscoveryResult generated = await pipeline.DiscoverAsync(
            Request("Generated/Generated.csproj"),
            TestContext.Current.CancellationToken);
        IntegrationDiscoveryResult excluded = await pipeline.DiscoverAsync(
            RequestWithExclusions(
                ["Excluded"],
                "Excluded/Excluded.csproj"),
            TestContext.Current.CancellationToken);

        Assert.Empty(generated.Findings);
        Assert.Empty(generated.Diagnostics);
        Assert.Empty(excluded.Findings);
        Assert.Empty(excluded.Diagnostics);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsUnsafeLiteralEvidence()
    {
        var pipeline = new IntegrationDiscoveryPipeline(
            [new ConfigurationKeyDetector("configuration", [])]);

        IntegrationDiscoveryResult result = await pipeline.DiscoverAsync(
            Request("Simple/Simple.csproj"),
            TestContext.Current.CancellationToken);
        string json = InspectionJsonSerializer.Serialize(Report(result));

        IntegrationFinding finding = Assert.Single(result.Findings);
        Assert.Equal("Messaging:OrdersTopic", finding.ConfigurationKey);
        Assert.DoesNotContain("fixture-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=db", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_ContainsDetectorFailuresAndContinues()
    {
        var pipeline = new IntegrationDiscoveryPipeline(
        [
            new ThrowingDetector(),
            new ConfigurationKeyDetector("working", [])
        ]);

        IntegrationDiscoveryResult result = await pipeline.DiscoverAsync(
            Request("Simple/Simple.csproj"),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Findings);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code == InspectionDiagnosticCodes.IntegrationDetectorFailed &&
                diagnostic.Context!["detector"] == "broken");
    }

    [Fact]
    public async Task DiscoverAsync_PropagatesCallerCancellation()
    {
        var pipeline = new IntegrationDiscoveryPipeline([new WaitingDetector()]);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.DiscoverAsync(
                Request("Simple/Simple.csproj"),
                cancellationSource.Token));
    }

    [Fact]
    public async Task DiscoverAsync_ConvertsDurationBudgetToPartialResult()
    {
        var pipeline = new IntegrationDiscoveryPipeline([new WaitingDetector()]);
        var options = new IntegrationDiscoveryOptions
        {
            MaxDuration = TimeSpan.FromMilliseconds(25)
        };

        IntegrationDiscoveryResult result = await pipeline.DiscoverAsync(
            RequestWithOptions(options, "Simple/Simple.csproj"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code == InspectionDiagnosticCodes.IntegrationDiscoveryLimitReached &&
                diagnostic.Context!["reason"] == "duration");
    }

    [Fact]
    public async Task DiscoverAsync_BoundsDiagnosticsAndSignalsTruncation()
    {
        var pipeline = new IntegrationDiscoveryPipeline(
            [new ConfigurationKeyDetector("configuration", [])]);
        var options = new IntegrationDiscoveryOptions
        {
            MaxBytesPerFile = 16,
            MaxDiagnostics = 1
        };

        IntegrationDiscoveryResult result = await pipeline.DiscoverAsync(
            RequestWithOptions(
                options,
                "Large/Large.csproj",
                "Simple/Simple.csproj"),
            TestContext.Current.CancellationToken);

        InspectionDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(InspectionDiagnosticCodes.IntegrationDiscoveryLimitReached, diagnostic.Code);
        Assert.Equal("diagnostics", diagnostic.Context!["reason"]);
        Assert.True(result.Truncated);
    }

    private static IntegrationDiscoveryRequest Request(params string[] projects) =>
        new(
            FixtureRoot,
            projects.Select(static path => new IntegrationDiscoveryProject(path)).ToArray());

    private static IntegrationDiscoveryRequest RequestWithOptions(
        IntegrationDiscoveryOptions options,
        params string[] projects) =>
        Request(projects) with
        {
            Options = options
        };

    private static IntegrationDiscoveryRequest RequestWithExclusions(
        IReadOnlyList<string> exclusions,
        params string[] projects) =>
        Request(projects) with
        {
            ExcludedPaths = exclusions
        };

    private static InspectionReport Report(IntegrationDiscoveryResult result) =>
        InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            result.Diagnostics) with
        {
            Integrations = result.Findings
        };

    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "IntegrationDiscovery");

    private sealed class ConfigurationKeyDetector(
        string id,
        List<string> invocationOrder) : IIntegrationDetector
    {
        public string Id => id;

        public ValueTask DetectAsync(
            IntegrationDetectionContext context,
            CancellationToken cancellationToken)
        {
            invocationOrder.Add(Id);
            foreach (LiteralExpressionSyntax literal in context.Root
                         .DescendantNodes()
                         .OfType<LiteralExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!context.Evidence.TryGetConfigurationKey(literal, out string key))
                {
                    continue;
                }

                var candidate = new IntegrationFindingCandidate(
                    IntegrationKind.Unknown,
                    IntegrationDirection.Unknown,
                    "fixture-detector",
                    context.GetOneBasedLine(literal),
                    IntegrationConfidence.Low,
                    ["fixture:configuration-key"],
                    ConfigurationKey: key,
                    Contract: "SameName");
                context.Findings.TryAdd(candidate);
                context.Findings.TryAdd(candidate);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingDetector : IIntegrationDetector
    {
        public string Id => "broken";

        public ValueTask DetectAsync(
            IntegrationDetectionContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic detector failure.");
    }

    private sealed class WaitingDetector : IIntegrationDetector
    {
        public string Id => "waiting";

        public async ValueTask DetectAsync(
            IntegrationDetectionContext context,
            CancellationToken cancellationToken) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
