using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class MsBuildProjectFactsEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_ReturnsNormalizedEffectiveProjectFacts()
    {
        var projectPath = FixturePath(
            "MSBuildEvaluation",
            "SampleProject",
            "SampleProject.csproj");

        var evaluator = new MsBuildProjectFactsEvaluator();
        var result = await evaluator.EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(result.Facts);
        Assert.False(string.IsNullOrWhiteSpace(result.Facts.ResolvedSdkVersion));
        Assert.Equal(
            new[] { new ProjectSdkReference("Microsoft.NET.Sdk") },
            result.Facts.DeclaredProjectSdks);
        Assert.Equal(new[] { "net10.0" }, result.Facts.TargetFrameworks);
        Assert.Equal("Exe", result.Facts.OutputType);
        Assert.True(result.Facts.IsTestProject is true);
        Assert.Null(result.Facts.UsingMicrosoftNETSdkWorker);
        Assert.Null(result.Facts.AzureFunctionsVersion);
        Assert.True(result.Facts.IsPackable is false);
        Assert.Equal(
            new[] { "linux-x64", "win-x64" },
            result.Facts.RuntimeIdentifiers);
    }

    [Fact]
    public async Task EvaluateAsync_NormalizesMultiTargetingAsACollection()
    {
        var projectPath = FixturePath(
            "ProjectKinds",
            "MultiTargeting",
            "MultiTargeting.csproj");

        var evaluator = new MsBuildProjectFactsEvaluator();
        var result = await evaluator.EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
        Assert.NotNull(result.Facts);
        Assert.Equal(
            new[] { "net8.0", "net10.0" },
            result.Facts.TargetFrameworks);
    }

    [Fact]
    public async Task EvaluateAsync_PropagatesInvalidProjectAsIdentifiableFailure()
    {
        var projectPath = FixturePath(
            "InvalidProject",
            "InvalidProject.csproj");

        var evaluator = new MsBuildProjectFactsEvaluator();
        var result = await evaluator.EvaluateAsync(
            projectPath,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(result.Facts);
        Assert.Equal(projectPath, result.ProjectPath);
        Assert.Equal(MsBuildEvaluationErrorCode.MsBuildEvaluationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task EvaluateAsync_CapturesDefinedAndInheritedPublicationFacts()
    {
        var facts = await EvaluateSuccessfulFactsAsync(
            "PublicationSettings",
            "Defined",
            "Defined.csproj");

        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(false),
            facts.Publication.SelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(true),
            facts.Publication.PublishSelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(false),
            facts.Publication.PublishAot);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(false),
            facts.Publication.PublishTrimmed);
        Assert.Equal(
            MsBuildStringPropertyValue.Evaluated("10.0.1"),
            facts.Publication.RuntimeFrameworkVersion);
        Assert.Equal(new[] { "linux-x64", "win-x64" }, facts.RuntimeIdentifiers);
    }

    [Fact]
    public async Task EvaluateAsync_DistinguishesEvaluatedFalseFromUnspecifiedPublicationValues()
    {
        var facts = await EvaluateSuccessfulFactsAsync(
            "PublicationSettings",
            "Absent",
            "Absent.csproj");

        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(false),
            facts.Publication.SelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Unspecified,
            facts.Publication.PublishSelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Unspecified,
            facts.Publication.PublishAot);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Unspecified,
            facts.Publication.PublishTrimmed);
        Assert.Equal(
            MsBuildStringPropertyValue.Unspecified,
            facts.Publication.RuntimeFrameworkVersion);
    }

    [Fact]
    public async Task EvaluateAsync_PreservesTargetFrameworkPublicationContexts()
    {
        var facts = await EvaluateSuccessfulFactsAsync(
            "PublicationSettings",
            "Conditional",
            "Conditional.csproj");

        Assert.Equal(
            MsBuildBooleanPropertyValue.ContextDependent,
            facts.Publication.SelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.ContextDependent,
            facts.Publication.PublishSelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.ContextDependent,
            facts.Publication.PublishAot);
        Assert.Equal(
            MsBuildBooleanPropertyValue.ContextDependent,
            facts.Publication.PublishTrimmed);
        Assert.Equal(
            MsBuildStringPropertyValue.ContextDependent,
            facts.Publication.RuntimeFrameworkVersion);
        Assert.Equal(new[] { "linux-x64", "win-x64" }, facts.RuntimeIdentifiers);

        var net8 = Assert.Single(
            facts.TargetFrameworkFacts,
            target => target.TargetFramework == "net8.0");
        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(false),
            net8.Publication.SelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Unspecified,
            net8.Publication.PublishSelfContained);
        Assert.Equal(new[] { "linux-x64" }, net8.RuntimeIdentifiers);

        var net10 = Assert.Single(
            facts.TargetFrameworkFacts,
            target => target.TargetFramework == "net10.0");
        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(true),
            net10.Publication.SelfContained);
        Assert.Equal(
            MsBuildBooleanPropertyValue.Evaluated(true),
            net10.Publication.PublishSelfContained);
        Assert.Equal(new[] { "linux-x64", "win-x64" }, net10.RuntimeIdentifiers);
    }

    [Fact]
    public async Task EvaluateAsync_RepresentsInvalidBooleanAsUnknown()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-repo-inspector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectPath = Path.Combine(temporaryDirectory, "Unknown.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                TestContext.Current.CancellationToken);
            var properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PublishAot"] = "not-a-boolean",
                ["TargetFramework"] = "net10.0"
            };
            var evaluator = new MsBuildProjectFactsEvaluator(
                new StubProjectEvaluator(MsBuildEvaluationResult.Success("10.0.100", properties)));

            var result = await evaluator.EvaluateAsync(
                projectPath,
                TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.NotNull(result.Facts);
            Assert.Equal(
                MsBuildBooleanPropertyValue.Unknown,
                result.Facts.Publication.PublishAot);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("$(RUNTIME_VERSION)")]
    [InlineData("fixture-secret")]
    [InlineData("10")]
    public async Task EvaluateAsync_RepresentsUnsafeRuntimeFrameworkVersionAsUnknown(string value)
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-repo-inspector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            string projectPath = Path.Combine(temporaryDirectory, "Unknown.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\" />",
                TestContext.Current.CancellationToken);
            var properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RuntimeFrameworkVersion"] = value,
                ["TargetFramework"] = "net10.0"
            };
            var evaluator = new MsBuildProjectFactsEvaluator(
                new StubProjectEvaluator(MsBuildEvaluationResult.Success("10.0.100", properties)));

            MsBuildProjectFactsResult result = await evaluator.EvaluateAsync(
                projectPath,
                TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.NotNull(result.Facts);
            Assert.Equal(
                MsBuildStringPropertyValue.Unknown,
                result.Facts.Publication.RuntimeFrameworkVersion);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_DistinguishesMissingBooleanAndNormalizesSingularValues()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-repo-inspector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var projectPath = Path.Combine(temporaryDirectory, "Sample.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><Sdk Name=\"Example.Project.Sdk\" Version=\"1.2.3\" /></Project>",
                TestContext.Current.CancellationToken);

            var rawProperties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AzureFunctionsVersion"] = " v4 ",
                ["TargetFramework"] = " net10.0 ",
                ["TargetFrameworks"] = string.Empty,
                ["OutputType"] = string.Empty,
                ["IsTestProject"] = string.Empty,
                ["UsingMicrosoftNETSdkWorker"] = " true ",
                ["IsPackable"] = "true",
                ["RuntimeIdentifier"] = "linux-x64",
                ["RuntimeIdentifiers"] = string.Empty
            };

            var projectEvaluator = new StubProjectEvaluator(
                MsBuildEvaluationResult.Success("10.0.100", rawProperties));
            var evaluator = new MsBuildProjectFactsEvaluator(projectEvaluator);

            var result = await evaluator.EvaluateAsync(
                projectPath,
                TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
            Assert.NotNull(result.Facts);
            Assert.Equal(
                new[]
                {
                    new ProjectSdkReference("Microsoft.NET.Sdk"),
                    new ProjectSdkReference("Example.Project.Sdk", "1.2.3")
                },
                result.Facts.DeclaredProjectSdks);
            Assert.Equal(new[] { "net10.0" }, result.Facts.TargetFrameworks);
            Assert.Null(result.Facts.OutputType);
            Assert.Null(result.Facts.IsTestProject);
            Assert.True(result.Facts.UsingMicrosoftNETSdkWorker is true);
            Assert.Equal("v4", result.Facts.AzureFunctionsVersion);
            Assert.True(result.Facts.IsPackable is true);
            Assert.Equal(new[] { "linux-x64" }, result.Facts.RuntimeIdentifiers);
            Assert.Equal(string.Empty, result.Facts.Properties["IsTestProject"]);
            Assert.Equal("true", result.Facts.Properties["IsPackable"]);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);

    private static async Task<MsBuildProjectFacts> EvaluateSuccessfulFactsAsync(
        params string[] fixturePathSegments)
    {
        var evaluator = new MsBuildProjectFactsEvaluator();
        var result = await evaluator.EvaluateAsync(
            FixturePath(fixturePathSegments),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "Project facts evaluation failed.");
        return Assert.IsType<MsBuildProjectFacts>(result.Facts);
    }

    private sealed class StubProjectEvaluator(MsBuildEvaluationResult result) : IMsBuildProjectEvaluator
    {
        public Task<MsBuildEvaluationResult> EvaluateAsync(
            MsBuildEvaluationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
