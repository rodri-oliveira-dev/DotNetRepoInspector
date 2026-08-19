using DotNetRepoInspector.Core.Classification;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class ProjectClassifierTests
{
    private static readonly string[] WebSdk = [DeterministicProjectClassifier.WebSdk];
    private static readonly string[] WorkerSdk = [DeterministicProjectClassifier.WorkerSdk];
    private static readonly string[] WebAndWorkerSdks =
    [
        DeterministicProjectClassifier.WebSdk,
        DeterministicProjectClassifier.WorkerSdk
    ];

    private readonly DeterministicProjectClassifier _classifier = new();

    [Fact]
    public void Classify_TestOverridesExecutableAndSpecializedSdkSignals()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebAndWorkerSdks,
            "Exe",
            true));

        Assert.Equal(ProjectClassificationKinds.Test, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal("property:IsTestProject=true", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_WebSdkIsRecognizedBeforeOutputType()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Web, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WebSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_WorkerSdkIsRecognizedBeforeOutputType()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WorkerSdk,
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Worker, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal(
            $"sdk:{DeterministicProjectClassifier.WorkerSdk}",
            Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_ExecutableWithoutSpecializedSdkIsConsole()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Console, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.Medium, classification.Confidence);
        Assert.Equal("property:OutputType=Exe", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_LibraryOutputTypeIsLibrary()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "Library",
            false));

        Assert.Equal(ProjectClassificationKinds.Library, classification.Kind);
        Assert.Equal(ProjectClassificationConfidence.High, classification.Confidence);
        Assert.Equal("property:OutputType=Library", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_ConflictingSpecializedSdksReturnUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            WebAndWorkerSdks,
            "Exe",
            false));

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Contains("conflict:specialized-sdk", classification.Signals);
        Assert.Contains($"sdk:{DeterministicProjectClassifier.WebSdk}", classification.Signals);
        Assert.Contains($"sdk:{DeterministicProjectClassifier.WorkerSdk}", classification.Signals);
    }

    [Fact]
    public void Classify_UnsupportedExecutableShapeReturnsUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            "WinExe",
            false));

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Equal("property:OutputType=WinExe", Assert.Single(classification.Signals));
    }

    [Fact]
    public void Classify_MissingEvidenceReturnsUnknown()
    {
        var classification = _classifier.Classify(new ProjectClassificationFacts(
            Array.Empty<string>(),
            null,
            null));

        Assert.Equal(ProjectClassificationKinds.Unknown, classification.Kind);
        Assert.Null(classification.Confidence);
        Assert.Empty(classification.Signals);
    }

    [Fact]
    public void Classify_IsDeterministicAcrossSdkOrderCasingAndDuplicates()
    {
        var first = _classifier.Classify(new ProjectClassificationFacts(
            new[]
            {
                "microsoft.net.sdk.web",
                "Microsoft.NET.Sdk.Web"
            },
            " exe ",
            false));
        var second = _classifier.Classify(new ProjectClassificationFacts(
            WebSdk,
            "Exe",
            false));

        Assert.Equal(second, first);
    }
}
