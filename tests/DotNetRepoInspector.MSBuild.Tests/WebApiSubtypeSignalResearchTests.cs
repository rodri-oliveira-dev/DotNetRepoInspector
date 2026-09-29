using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class WebApiSubtypeSignalResearchTests
{
    private static readonly string[] ResearchProperties =
    [
        "OutputType",
        "AddRazorSupportForMvc"
    ];

    [Fact]
    public async Task WebSdkExecutable_RemainsBaseWebWithoutSubtype()
    {
        string projectPath = FixturePath(
            "WebApiSubtypeSignals",
            "WebSdkExecutable",
            "WebSdkExecutable.csproj");

        MsBuildProjectFacts facts = await EvaluateSuccessfulFactsAsync(projectPath);
        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(facts);

        Assert.Contains(
            facts.DeclaredProjectSdks,
            sdk => string.Equals(
                sdk.Name,
                DeterministicProjectClassifier.WebSdk,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Exe", facts.OutputType);
        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public async Task RazorSupportOverlap_DoesNotCreatePositiveOrNegativeWebApiInference()
    {
        string projectPath = FixturePath(
            "WebApiSubtypeSignals",
            "RazorSupportOverlap",
            "RazorSupportOverlap.csproj");

        var evaluator = new DotNetMsBuildProjectEvaluator();
        MsBuildEvaluationResult raw = await evaluator.EvaluateAsync(
            new MsBuildEvaluationRequest(
                projectPath,
                ResearchProperties,
                Array.Empty<string>()),
            TestContext.Current.CancellationToken);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("Exe", raw.Properties["OutputType"]);
        Assert.Equal("true", raw.Properties["AddRazorSupportForMvc"]);

        MsBuildProjectFacts facts = await EvaluateSuccessfulFactsAsync(projectPath);
        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(facts);

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    private static async Task<MsBuildProjectFacts> EvaluateSuccessfulFactsAsync(string projectPath)
    {
        MsBuildProjectFactsResult result = await new MsBuildProjectFactsEvaluator().EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
        return Assert.IsType<MsBuildProjectFacts>(result.Facts);
    }

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);
}
