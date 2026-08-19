using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class DotNetMsBuildProjectEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_EvaluatesProjectImportsAndConditions()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MSBuildEvaluation",
            "SampleProject",
            "SampleProject.csproj");

        var evaluator = new DotNetMsBuildProjectEvaluator();
        var result = await evaluator.EvaluateAsync(
            new MsBuildEvaluationRequest(
                projectPath,
                new[]
                {
                    "TargetFramework",
                    "InspectorInheritedProperty",
                    "InspectorConditionalProperty"
                }));

        Assert.True(result.Succeeded, result.Error?.Message ?? "MSBuild evaluation failed.");
        Assert.False(string.IsNullOrWhiteSpace(result.ResolvedSdkVersion));
        Assert.Equal("net10.0", result.Properties["TargetFramework"]);
        Assert.Equal("from-directory-build-props", result.Properties["InspectorInheritedProperty"]);
        Assert.Equal("condition-evaluated", result.Properties["InspectorConditionalProperty"]);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsProjectNotFoundForMissingProject()
    {
        var evaluator = new DotNetMsBuildProjectEvaluator();
        var result = await evaluator.EvaluateAsync(
            new MsBuildEvaluationRequest(
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.csproj"),
                new[] { "TargetFramework" }));

        Assert.False(result.Succeeded);
        Assert.Equal(MsBuildEvaluationErrorCode.ProjectNotFound, result.Error?.Code);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsInvalidPropertyNamesBeforeStartingMsBuild()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MSBuildEvaluation",
            "SampleProject",
            "SampleProject.csproj");

        var evaluator = new DotNetMsBuildProjectEvaluator();
        var result = await evaluator.EvaluateAsync(
            new MsBuildEvaluationRequest(
                projectPath,
                new[] { "TargetFramework;Build" }));

        Assert.False(result.Succeeded);
        Assert.Equal(MsBuildEvaluationErrorCode.InvalidRequest, result.Error?.Code);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsDotNetHostNotFoundWhenDotNetCannotStart()
    {
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MSBuildEvaluation",
            "SampleProject",
            "SampleProject.csproj");

        var evaluator = new DotNetMsBuildProjectEvaluator($"dotnet-missing-{Guid.NewGuid():N}");
        var result = await evaluator.EvaluateAsync(
            new MsBuildEvaluationRequest(projectPath, new[] { "TargetFramework" }));

        Assert.False(result.Succeeded);
        Assert.Equal(MsBuildEvaluationErrorCode.DotNetHostNotFound, result.Error?.Code);
    }
}
