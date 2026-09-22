using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class TestProjectSignalResearchTests
{
    private static readonly string[] ResearchProperties =
    [
        "IsTestProject",
        "IsTestingPlatformApplication",
        "OutputType"
    ];

    private static readonly string[] ResearchItems = ["PackageReference"];

    [Fact]
    public async Task MtpApplication_ReproducesCurrentFalseNegativeWithoutIsTestProject()
    {
        string projectPath = FixturePath(
            "TestProjectSignals",
            "MtpApplication",
            "MtpApplication.csproj");

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal(string.Empty, raw.Properties["IsTestProject"]);
        Assert.Equal("true", raw.Properties["IsTestingPlatformApplication"]);
        Assert.Equal("Exe", raw.Properties["OutputType"]);

        var factsEvaluator = new MsBuildProjectFactsEvaluator();
        MsBuildProjectFactsResult factsResult = await factsEvaluator.EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(
            factsResult.Succeeded,
            factsResult.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(factsResult.Facts);
        Assert.Null(factsResult.Facts.IsTestProject);

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(factsResult.Facts);

        Assert.Equal(ProjectClassificationKinds.Console, classification.Kind);
    }

    [Fact]
    public async Task TestSdkFallback_ExposesDirectPackageReferenceWhenIsTestProjectIsMissing()
    {
        string projectPath = FixturePath(
            "TestProjectSignals",
            "TestSdkFallback",
            "TestSdkFallback.csproj");

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal(string.Empty, raw.Properties["IsTestProject"]);
        Assert.Equal(string.Empty, raw.Properties["IsTestingPlatformApplication"]);
        Assert.Equal("Library", raw.Properties["OutputType"]);
        Assert.Contains(
            raw.Items["PackageReference"],
            item => string.Equals(
                item.Identity,
                "Microsoft.NET.Test.Sdk",
                StringComparison.OrdinalIgnoreCase));

        var factsEvaluator = new MsBuildProjectFactsEvaluator();
        MsBuildProjectFactsResult factsResult = await factsEvaluator.EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(
            factsResult.Succeeded,
            factsResult.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(factsResult.Facts);

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(factsResult.Facts);

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
    }

    [Fact]
    public async Task ExplicitFalseConflict_KeepsNegativePropertyAlongsidePackageHint()
    {
        string projectPath = FixturePath(
            "TestProjectSignals",
            "ExplicitFalseConflict",
            "ExplicitFalseConflict.csproj");

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("false", raw.Properties["IsTestProject"]);
        Assert.Equal(string.Empty, raw.Properties["IsTestingPlatformApplication"]);
        Assert.Contains(
            raw.Items["PackageReference"],
            item => string.Equals(
                item.Identity,
                "Microsoft.NET.Test.Sdk",
                StringComparison.OrdinalIgnoreCase));
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

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);
}
