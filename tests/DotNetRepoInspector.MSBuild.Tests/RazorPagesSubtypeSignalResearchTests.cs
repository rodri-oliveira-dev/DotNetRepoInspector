using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class RazorPagesSubtypeSignalResearchTests
{
    private static readonly string[] ResearchProperties =
    [
        "OutputType",
        "AddRazorSupportForMvc"
    ];

    private static readonly string[] ResearchItems =
    [
        "RazorGenerate"
    ];

    [Fact]
    public async Task RazorGenerate_ContainsRazorPageAndMvcViewWithoutSubtypeEvidence()
    {
        string projectPath = FixturePath(
            "RazorPagesSubtypeSignals",
            "RazorGenerateOverlap",
            "RazorGenerateOverlap.csproj");

        string pagePath = FixturePath(
            "RazorPagesSubtypeSignals",
            "RazorGenerateOverlap",
            "Pages",
            "PageSample.cshtml");
        string viewPath = FixturePath(
            "RazorPagesSubtypeSignals",
            "RazorGenerateOverlap",
            "Views",
            "ViewSample.cshtml");

        Assert.StartsWith("@page", File.ReadAllText(pagePath), StringComparison.Ordinal);
        Assert.DoesNotContain("@page", File.ReadAllText(viewPath), StringComparison.Ordinal);

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("Exe", raw.Properties["OutputType"]);
        Assert.Equal("true", raw.Properties["AddRazorSupportForMvc"]);

        IReadOnlyList<MsBuildEvaluationItem> razorItems = raw.Items["RazorGenerate"];
        Assert.Equal(2, razorItems.Count);

        string[] identities = razorItems
            .Select(item => NormalizeIdentity(item.Identity))
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(
            identities,
            identity => identity.EndsWith(
                "Pages/PageSample.cshtml",
                StringComparison.Ordinal));
        Assert.Contains(
            identities,
            identity => identity.EndsWith(
                "Views/ViewSample.cshtml",
                StringComparison.Ordinal));

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
    public async Task RazorClassLibrary_CanExposeRazorPageBuildSignalsWithoutWebSubtype()
    {
        string projectPath = FixturePath(
            "RazorPagesSubtypeSignals",
            "RazorClassLibraryOverlap",
            "RazorClassLibraryOverlap.csproj");

        MsBuildEvaluationResult raw = await EvaluateResearchSignalsAsync(projectPath);

        Assert.True(raw.Succeeded, raw.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.Equal("Library", raw.Properties["OutputType"]);
        Assert.Equal("true", raw.Properties["AddRazorSupportForMvc"]);

        MsBuildEvaluationItem razorItem = Assert.Single(raw.Items["RazorGenerate"]);
        Assert.EndsWith(
            "Pages/LibraryPage.cshtml",
            NormalizeIdentity(razorItem.Identity),
            StringComparison.Ordinal);

        MsBuildProjectFacts facts = await EvaluateSuccessfulFactsAsync(projectPath);
        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(facts);

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Equal(
            "property:OutputType=Library",
            Assert.Single(classification.Signals));
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
