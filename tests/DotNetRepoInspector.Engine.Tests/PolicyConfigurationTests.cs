using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Core.Policies;

using Xunit;

namespace DotNetRepoInspector.Engine.Tests;

public sealed class PolicyConfigurationTests
{
    [Fact]
    public async Task ResolveAsync_NoConfiguration_KeepsPoliciesDisabled()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory(
            "DotNetRepoInspector-PolicyConfig-").FullName;

        try
        {
            var configuration = await InspectionConfigurationResolver.ResolveAsync(
                repositoryRoot,
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.True(configuration.Succeeded);
            Assert.Empty(configuration.PolicyRules);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_EnabledTargetFrameworkPolicy_ParsesAndEvaluates()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory(
            "DotNetRepoInspector-PolicyConfig-").FullName;

        try
        {
            await WriteConfigurationAsync(
                repositoryRoot,
                """
                {
                  "schemaVersion": "2",
                  "policies": {
                    "targetFramework": {
                      "enabled": true,
                      "allowed": ["net8.0", "NET10.0", "net10.0"],
                      "severity": "warning"
                    }
                  }
                }
                """);

            var configuration = await InspectionConfigurationResolver.ResolveAsync(
                repositoryRoot,
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.True(configuration.Succeeded);
            var rule = Assert.IsType<TargetFrameworkPolicyRule>(
                Assert.Single(configuration.PolicyRules));
            Assert.Equal(new[] { "net10.0", "net8.0" }, rule.AllowedTargetFrameworks);
            Assert.Equal(PolicySeverity.Warning, rule.Severity);

            var result = new PolicyEngine(configuration.PolicyRules).Evaluate(
                CreateReport("net7.0"));

            var finding = Assert.Single(result.Findings);
            Assert.Equal(TargetFrameworkPolicyRule.RuleCode, finding.RuleCode);
            Assert.Equal(PolicySeverity.Warning, finding.Severity);
            Assert.Equal("src/App/App.csproj", finding.Scope.ProjectPath);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_DisabledTargetFrameworkPolicy_DoesNotRegisterRule()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory(
            "DotNetRepoInspector-PolicyConfig-").FullName;

        try
        {
            await WriteConfigurationAsync(
                repositoryRoot,
                """
                {
                  "schemaVersion": "2",
                  "policies": {
                    "targetFramework": {
                      "enabled": false
                    }
                  }
                }
                """);

            var configuration = await InspectionConfigurationResolver.ResolveAsync(
                repositoryRoot,
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.True(configuration.Succeeded);
            Assert.Empty(configuration.PolicyRules);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        """
        {
          "schemaVersion": "1",
          "policies": {
            "targetFramework": {
              "enabled": true,
              "allowed": ["net10.0"]
            }
          }
        }
        """,
        "policies-require-config-schema-2")]
    [InlineData(
        """
        {
          "schemaVersion": "2",
          "policies": {
            "targetFramework": {
              "allowed": ["net10.0"]
            }
          }
        }
        """,
        "target-framework-policy-enabled-required")]
    [InlineData(
        """
        {
          "schemaVersion": "2",
          "policies": {
            "targetFramework": {
              "enabled": true
            }
          }
        }
        """,
        "target-framework-policy-allowed-required")]
    [InlineData(
        """
        {
          "schemaVersion": "2",
          "policies": {
            "targetFramework": {
              "enabled": true,
              "allowed": ["net10.0"],
              "severity": "critical"
            }
          }
        }
        """,
        "invalid-target-framework-policy-severity")]
    public async Task ResolveAsync_InvalidPolicyConfiguration_ReturnsStableDiagnostic(
        string json,
        string expectedReason)
    {
        var repositoryRoot = Directory.CreateTempSubdirectory(
            "DotNetRepoInspector-PolicyConfig-").FullName;

        try
        {
            await WriteConfigurationAsync(repositoryRoot, json);

            var configuration = await InspectionConfigurationResolver.ResolveAsync(
                repositoryRoot,
                new RepositoryInspectionRequest(repositoryRoot),
                TestContext.Current.CancellationToken);

            Assert.False(configuration.Succeeded);
            Assert.Empty(configuration.PolicyRules);
            Assert.NotNull(configuration.Error);
            Assert.Equal(InspectionDiagnosticCodes.InvalidConfiguration, configuration.Error.Code);
            Assert.Equal(InspectionDiagnosticSeverity.Error, configuration.Error.Severity);
            Assert.Equal(expectedReason, configuration.Error.Context!["reason"]);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static Task WriteConfigurationAsync(string repositoryRoot, string json) =>
        File.WriteAllTextAsync(
            Path.Combine(repositoryRoot, InspectionConfigurationResolver.DefaultFileName),
            json,
            TestContext.Current.CancellationToken);

    private static InspectionReport CreateReport(string targetFramework) =>
        InspectionReport.Create(
            new RepositoryMetadata("sample", null, null, null, false),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [
                new ProjectInspection(
                    "src/App/App.csproj",
                    "App",
                    "10.0.400",
                    [new ProjectSdkMetadata("Microsoft.NET.Sdk", null)],
                    [targetFramework],
                    "Library",
                    false,
                    true,
                    [],
                    new ProjectClassification("library", "high", ["output-type:Library"]),
                    [],
                    [])
            ],
            []);
}
