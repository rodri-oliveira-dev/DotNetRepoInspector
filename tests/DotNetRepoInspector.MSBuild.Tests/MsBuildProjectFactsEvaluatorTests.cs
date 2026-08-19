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
                ["TargetFramework"] = " net10.0 ",
                ["TargetFrameworks"] = string.Empty,
                ["OutputType"] = string.Empty,
                ["IsTestProject"] = string.Empty,
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

    private sealed class StubProjectEvaluator(MsBuildEvaluationResult result) : IMsBuildProjectEvaluator
    {
        public Task<MsBuildEvaluationResult> EvaluateAsync(
            MsBuildEvaluationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
