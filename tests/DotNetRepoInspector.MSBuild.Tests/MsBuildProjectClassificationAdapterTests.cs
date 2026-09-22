using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.MSBuild.Classification;
using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class MsBuildProjectClassificationAdapterTests
{
    private static readonly IReadOnlyDictionary<string, string> NoProperties =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly MsBuildProjectClassificationAdapter _adapter = new();

    [Fact]
    public void Classify_MapsEvaluatedFactsToCoreClassifier()
    {
        var facts = CreateFacts(
            new[] { new ProjectSdkReference(DeterministicProjectClassifier.WorkerSdk) },
            "Exe",
            false,
            NoProperties);

        var classification = _adapter.Classify(facts);

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
    }

    [Fact]
    public void Classify_MapsTestingPlatformAndPackageFactsToCoreClassifier()
    {
        var mtpFacts = CreateFacts(
            new[] { new ProjectSdkReference("Microsoft.NET.Sdk") },
            "Exe",
            false,
            NoProperties) with
        {
            IsTestingPlatformApplication = true
        };

        var mtpClassification = _adapter.Classify(mtpFacts);

        Assert.Equal(ProjectClassificationKinds.Test, mtpClassification.Kind);
        Assert.Equal(
            "property:IsTestingPlatformApplication=true",
            Assert.Single(mtpClassification.Signals));

        var packageFacts = CreateFacts(
            new[] { new ProjectSdkReference("Microsoft.NET.Sdk") },
            "Library",
            null,
            NoProperties) with
        {
            PackageReferences = [DeterministicProjectClassifier.MicrosoftNetTestSdkPackage]
        };

        var packageClassification = _adapter.Classify(packageFacts);

        Assert.Equal(ProjectClassificationKinds.Test, packageClassification.Kind);
        Assert.Equal(
            ProjectClassificationConfidence.Medium,
            packageClassification.Confidence);
        Assert.Equal(
            "package:Microsoft.NET.Test.Sdk",
            Assert.Single(packageClassification.Signals));
    }

    [Fact]
    public void Classify_DoesNotUseSuggestiveRawPropertiesAsClassificationHeuristics()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AssemblyName"] = "Payments.Tests",
            ["CustomWorkerHint"] = "true"
        };
        var facts = CreateFacts(
            new[] { new ProjectSdkReference("Microsoft.NET.Sdk") },
            "Library",
            false,
            properties);

        var classification = _adapter.Classify(facts);

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal("property:OutputType=Library", Assert.Single(classification.Signals));
    }

    private static MsBuildProjectFacts CreateFacts(
        IReadOnlyList<ProjectSdkReference> sdks,
        string? outputType,
        bool? isTestProject,
        IReadOnlyDictionary<string, string> properties) =>
        new(
            "10.0.400",
            sdks,
            Array.Empty<string>(),
            outputType,
            isTestProject,
            null,
            Array.Empty<string>(),
            properties);
}
