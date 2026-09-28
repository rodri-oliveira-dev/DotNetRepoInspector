using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class WorkerProjectSignalResearchTests
{
    private static readonly string[] ResearchProperties =
    [
        "OutputType",
        "UsingMicrosoftNETSdkWorker"
    ];

    private static readonly string[] ResearchItems = ["PackageReference"];

    [Fact]
    public async Task ExplicitWorkerOptInProperty_ClassifiesWorkerWithHighConfidence()
    {
        string projectPath = FixturePath(
            "WorkerProjectSignals",
            "UsingWorkerProperty",
            "UsingWorkerProperty.csproj");

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("true", raw.Properties["UsingMicrosoftNETSdkWorker"]);
        Assert.Equal("Exe", raw.Properties["OutputType"]);

        MsBuildProjectFactsResult factsResult = await EvaluateFactsAsync(projectPath);

        Assert.True(
            factsResult.Succeeded,
            factsResult.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(factsResult.Facts);
        Assert.True(factsResult.Facts.UsingMicrosoftNETSdkWorker is true);

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(factsResult.Facts);

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            "property:UsingMicrosoftNETSdkWorker=true",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public async Task SystemdService_ClassifiesWorkerWithMediumConfidence()
    {
        string projectPath = FixturePath(
            "WorkerProjectSignals",
            "SystemdService",
            "SystemdService.csproj");

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("Exe", raw.Properties["OutputType"]);
        AssertPackageReference(raw, "Microsoft.Extensions.Hosting.Systemd");

        MsBuildProjectFactsResult factsResult = await EvaluateFactsAsync(projectPath);

        Assert.True(
            factsResult.Succeeded,
            factsResult.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(factsResult.Facts);
        Assert.Contains(
            factsResult.Facts.PackageReferences,
            package => string.Equals(
                package,
                "Microsoft.Extensions.Hosting.Systemd",
                StringComparison.OrdinalIgnoreCase));

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(factsResult.Facts);

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Equal(
            "package:Microsoft.Extensions.Hosting.Systemd",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public async Task WindowsService_ClassifiesWorkerWithMediumConfidence()
    {
        string projectPath = FixturePath(
            "WorkerProjectSignals",
            "WindowsService",
            "WindowsService.csproj");

        MsBuildProjectFactsResult factsResult = await EvaluateFactsAsync(projectPath);

        Assert.True(
            factsResult.Succeeded,
            factsResult.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(factsResult.Facts);
        Assert.Contains(
            factsResult.Facts.PackageReferences,
            package => string.Equals(
                package,
                "Microsoft.Extensions.Hosting.WindowsServices",
                StringComparison.OrdinalIgnoreCase));

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(factsResult.Facts);

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Equal(
            "package:Microsoft.Extensions.Hosting.WindowsServices",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public async Task GenericHostingPackage_RemainsAmbiguousAndConsole()
    {
        string projectPath = FixturePath(
            "WorkerProjectSignals",
            "HostingOnlyAmbiguous",
            "HostingOnlyAmbiguous.csproj");

        MsBuildProjectFactsResult factsResult = await EvaluateFactsAsync(projectPath);

        Assert.True(
            factsResult.Succeeded,
            factsResult.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(factsResult.Facts);
        Assert.Contains(
            factsResult.Facts.PackageReferences,
            package => string.Equals(
                package,
                "Microsoft.Extensions.Hosting",
                StringComparison.OrdinalIgnoreCase));

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(factsResult.Facts);

        Assert.Equal(ProjectClassificationKinds.Console, classification.Kind);
        Assert.Equal(
            "property:OutputType=Exe",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public async Task WebConflict_ExposesExplicitWorkerOptInAndClassifiesUnknown()
    {
        string projectPath = FixturePath(
            "WorkerProjectSignals",
            "WebConflict",
            "WebConflict.csproj");

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("true", raw.Properties["UsingMicrosoftNETSdkWorker"]);

        MsBuildProjectFactsResult factsResult = await EvaluateFactsAsync(projectPath);

        Assert.True(
            factsResult.Succeeded,
            factsResult.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(factsResult.Facts);
        Assert.Contains(
            factsResult.Facts.DeclaredProjectSdks,
            sdk => string.Equals(
                sdk.Name,
                DeterministicProjectClassifier.WebSdk,
                StringComparison.OrdinalIgnoreCase));

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(factsResult.Facts);

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Contains("conflict:web-worker", classification.Signals);
    }

    private static Task<MsBuildEvaluationResult> EvaluateResearchSignalsAsync(string projectPath)
    {
        var evaluator = new DotNetMsBuildProjectEvaluator();
        return evaluator.EvaluateAsync(
            new MsBuildEvaluationRequest(
                projectPath,
                ResearchProperties,
                ResearchItems),
            TestContext.Current.CancellationToken);
    }

    private static Task<MsBuildProjectFactsResult> EvaluateFactsAsync(string projectPath) =>
        new MsBuildProjectFactsEvaluator().EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

    private static void AssertPackageReference(
        MsBuildEvaluationResult evaluation,
        string packageId)
    {
        Assert.Contains(
            evaluation.Items["PackageReference"],
            item => string.Equals(
                item.Identity,
                packageId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);
}
