using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class BlazorServerSubtypeSignalResearchTests
{
    private static readonly string[] ResearchProperties =
    [
        "OutputType",
        "AddRazorSupportForMvc"
    ];

    private static readonly string[] ResearchItems =
    [
        "Content",
        "RazorComponent"
    ];

    [Fact]
    public async Task BlazorWebApp_SourceGroundTruthRemainsBaseWebWithoutSubtype()
    {
        string projectPath = FixturePath(
            "BlazorServerSubtypeSignals",
            "WebAppStaticSsr",
            "WebAppStaticSsr.csproj");
        string programPath = FixturePath(
            "BlazorServerSubtypeSignals",
            "WebAppStaticSsr",
            "Program.cs");

        string program = File.ReadAllText(programPath);
        Assert.Contains("AddRazorComponents", program, StringComparison.Ordinal);
        Assert.Contains("MapRazorComponents", program, StringComparison.Ordinal);

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);
        AssertCommonWebComponentBoundary(raw);

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

    [Fact]
    public async Task InteractiveServer_SourceGroundTruthRemainsBaseWebWithoutSubtype()
    {
        string projectPath = FixturePath(
            "BlazorServerSubtypeSignals",
            "InteractiveServer",
            "InteractiveServer.csproj");
        string programPath = FixturePath(
            "BlazorServerSubtypeSignals",
            "InteractiveServer",
            "Program.cs");

        string program = File.ReadAllText(programPath);
        Assert.Contains("AddInteractiveServerComponents", program, StringComparison.Ordinal);
        Assert.Contains("AddInteractiveServerRenderMode", program, StringComparison.Ordinal);

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);
        AssertCommonWebComponentBoundary(raw);

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

    private static void AssertCommonWebComponentBoundary(MsBuildEvaluationResult raw)
    {
        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("Exe", raw.Properties["OutputType"]);
        Assert.Equal("true", raw.Properties["AddRazorSupportForMvc"]);

        Assert.Contains(
            raw.Items["Content"],
            item => NormalizeIdentity(item.Identity).EndsWith(
                "Components/App.razor",
                StringComparison.Ordinal));
        Assert.Empty(raw.Items["RazorComponent"]);
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

    private static async Task<MsBuildProjectFacts> EvaluateSuccessfulFactsAsync(string projectPath)
    {
        MsBuildProjectFactsResult result = await new MsBuildProjectFactsEvaluator().EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
        return Assert.IsType<MsBuildProjectFacts>(result.Facts);
    }

    private static string NormalizeIdentity(string identity) =>
        identity.Replace('\\', '/');

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);
}
