using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class AzureFunctionsSubtypeSignalTests
{
    [Fact]
    public async Task ModernIsolatedSdk_ProducesDeterministicWorkerSubtype()
    {
        string projectPath = FixturePath(
            "AzureFunctionsSubtypeSignals",
            "IsolatedModernSdk",
            "IsolatedModernSdk.csproj");

        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AzureFunctionsVersion"] = "v4",
            ["OutputType"] = "Exe",
            ["TargetFramework"] = "net10.0"
        };
        var items = new Dictionary<string, IReadOnlyList<MsBuildEvaluationItem>>(
            StringComparer.Ordinal)
        {
            ["PackageReference"] =
            [
                new MsBuildEvaluationItem(
                    DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerPackage,
                    new Dictionary<string, string>(StringComparer.Ordinal))
            ],
            ["ProjectReference"] = []
        };
        var evaluator = new MsBuildProjectFactsEvaluator(
            new StubProjectEvaluator(
                MsBuildEvaluationResult.Success("10.0.400", properties, items)));

        MsBuildProjectFactsResult result = await evaluator.EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
        MsBuildProjectFacts facts = Assert.IsType<MsBuildProjectFacts>(result.Facts);
        Assert.Contains(
            facts.DeclaredProjectSdks,
            sdk => string.Equals(
                sdk.Name,
                DeterministicProjectClassifier.AzureFunctionsSdk,
                StringComparison.OrdinalIgnoreCase));

        ProjectClassification classification =
            new MsBuildProjectClassificationAdapter().Classify(facts);

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            ProjectClassificationSubtypes.AzureFunctionsIsolated,
            classification.Subtype);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.AzureFunctionsSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public async Task LegacyIsolatedSignals_ProduceDeterministicWorkerSubtype()
    {
        ProjectClassification classification = await ClassifyFixtureAsync(
            "IsolatedLegacy",
            "IsolatedLegacy.csproj");

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            ProjectClassificationSubtypes.AzureFunctionsIsolated,
            classification.Subtype);
        Assert.Contains(
            "property:AzureFunctionsVersion=v4",
            classification.Signals);
        Assert.Contains(
            $"package:{DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerPackage}",
            classification.Signals);
        Assert.Contains(
            $"package:{DeterministicProjectClassifier.MicrosoftAzureFunctionsWorkerSdkPackage}",
            classification.Signals);
    }

    [Fact]
    public async Task InProcessSignals_ProduceDeterministicLibrarySubtype()
    {
        ProjectClassification classification = await ClassifyFixtureAsync(
            "InProcess",
            "InProcess.csproj");

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            ProjectClassificationSubtypes.AzureFunctionsInProcess,
            classification.Subtype);
        Assert.Equal(
            [
                "property:AzureFunctionsVersion=v4",
                $"package:{DeterministicProjectClassifier.MicrosoftNetSdkFunctionsPackage}"
            ],
            classification.Signals);
    }

    [Fact]
    public async Task AzureFunctionsVersionAlone_RemainsWithoutSubtype()
    {
        ProjectClassification classification = await ClassifyFixtureAsync(
            "VersionOnly",
            "VersionOnly.csproj");

        Assert.Equal(ProjectClassificationKinds.Console, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Null(classification.Subtype);
    }

    [Fact]
    public async Task MixedExecutionModelSignals_ReturnUnknownWithoutSubtype()
    {
        ProjectClassification classification = await ClassifyFixtureAsync(
            "MixedModels",
            "MixedModels.csproj");

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Null(classification.Subtype);
        Assert.Contains("conflict:azure-functions-model", classification.Signals);
    }

    private static async Task<ProjectClassification> ClassifyFixtureAsync(
        string fixture,
        string projectFile)
    {
        string projectPath = FixturePath(
            "AzureFunctionsSubtypeSignals",
            fixture,
            projectFile);

        MsBuildProjectFactsResult result = await new MsBuildProjectFactsEvaluator().EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
        MsBuildProjectFacts facts = Assert.IsType<MsBuildProjectFacts>(result.Facts);

        Assert.Equal("v4", facts.AzureFunctionsVersion);

        return new MsBuildProjectClassificationAdapter().Classify(facts);
    }

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);

    private sealed class StubProjectEvaluator(MsBuildEvaluationResult result)
        : IMsBuildProjectEvaluator
    {
        public Task<MsBuildEvaluationResult> EvaluateAsync(
            MsBuildEvaluationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
