using DotNetRepoInspector.MSBuild.Evaluation;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class SecurityHardeningTests
{
    private const string SecretEnvironmentName = "DRI_SECURITY_TEST_ACCESS_TOKEN";
    private const string SafeEnvironmentName = "DRI_SECURITY_TEST_SETTING";

    [Fact]
    public async Task EvaluateAsync_RemovesCredentialsAndDisablesNodeReuse()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Security-").FullName;
        var previousSecret = Environment.GetEnvironmentVariable(SecretEnvironmentName);
        var previousSafe = Environment.GetEnvironmentVariable(SafeEnvironmentName);

        try
        {
            Environment.SetEnvironmentVariable(SecretEnvironmentName, "must-not-reach-msbuild");
            Environment.SetEnvironmentVariable(SafeEnvironmentName, "safe-setting");

            var projectPath = Path.Combine(repositoryRoot, "SecurityProbe.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ObservedSecret>$({{SecretEnvironmentName}})</ObservedSecret>
                    <ObservedSafe>$({{SafeEnvironmentName}})</ObservedSafe>
                    <ObservedNodeReuse>$(MSBUILDDISABLENODEREUSE)</ObservedNodeReuse>
                  </PropertyGroup>
                </Project>
                """,
                TestContext.Current.CancellationToken);

            var evaluator = new DotNetMsBuildProjectEvaluator();
            var result = await evaluator.EvaluateAsync(
                new MsBuildEvaluationRequest(
                    projectPath,
                    ["ObservedSecret", "ObservedSafe", "ObservedNodeReuse"]),
                TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.Error?.Message ?? "MSBuild evaluation failed.");
            Assert.Equal(string.Empty, result.Properties["ObservedSecret"]);
            Assert.Equal("safe-setting", result.Properties["ObservedSafe"]);
            Assert.Equal("1", result.Properties["ObservedNodeReuse"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecretEnvironmentName, previousSecret);
            Environment.SetEnvironmentVariable(SafeEnvironmentName, previousSafe);
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }
}
