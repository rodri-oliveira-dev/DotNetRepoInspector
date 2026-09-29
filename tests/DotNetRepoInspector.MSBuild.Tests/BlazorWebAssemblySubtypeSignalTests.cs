using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class BlazorWebAssemblySubtypeSignalTests
{
    [Fact]
    public async Task BlazorWebAssemblySdk_ProducesDeterministicWebSubtype()
    {
        string projectPath = FixturePath(
            "BlazorWebAssemblySubtypeSignals",
            "StandaloneSdk",
            "StandaloneSdk.csproj");

        MsBuildProjectFacts facts = await EvaluateSuccessfulFactsAsync(projectPath);
        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(facts);

        Assert.Contains(
            facts.DeclaredProjectSdks,
            sdk => string.Equals(
                sdk.Name,
                DeterministicProjectClassifier.BlazorWebAssemblySdk,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(ProjectClassificationSubtypes.BlazorWebAssembly, classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.BlazorWebAssemblySdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public async Task RazorComponentLibrary_DoesNotInferBlazorWebAssemblySubtype()
    {
        string projectPath = FixturePath(
            "BlazorWebAssemblySubtypeSignals",
            "RazorLibraryAmbiguous",
            "RazorLibraryAmbiguous.csproj");

        MsBuildProjectFacts facts = await EvaluateSuccessfulFactsAsync(projectPath);
        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(facts);

        Assert.Contains(
            facts.DeclaredProjectSdks,
            sdk => string.Equals(
                sdk.Name,
                "Microsoft.NET.Sdk.Razor",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            facts.DeclaredProjectSdks,
            sdk => string.Equals(
                sdk.Name,
                DeterministicProjectClassifier.BlazorWebAssemblySdk,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            "property:OutputType=Library",
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
