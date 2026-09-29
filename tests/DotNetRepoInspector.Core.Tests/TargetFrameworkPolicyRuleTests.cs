using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Core.Policies;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class TargetFrameworkPolicyRuleTests
{
    [Fact]
    public void Evaluate_SingleTargetAllowed_ReturnsNoFinding()
    {
        var engine = new PolicyEngine(
            [new TargetFrameworkPolicyRule(["net8.0", "net10.0"])]);

        var result = engine.Evaluate(CreateReport(
            CreateProject("src/App/App.csproj", ["net10.0"])));

        Assert.True(result.IsCompliant);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Evaluate_SingleTargetNotAllowed_ReturnsStructuredProjectError()
    {
        var engine = new PolicyEngine(
            [new TargetFrameworkPolicyRule(["net8.0", "net10.0"])]);

        var result = engine.Evaluate(CreateReport(
            CreateProject("src/Legacy/Legacy.csproj", ["net7.0"])));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(TargetFrameworkPolicyRule.RuleCode, finding.RuleCode);
        Assert.Equal(PolicySeverity.Error, finding.Severity);
        Assert.Equal(PolicyFindingScope.ProjectKind, finding.Scope.Kind);
        Assert.Equal("src/Legacy/Legacy.csproj", finding.Scope.ProjectPath);
        Assert.Equal("net10.0,net8.0", finding.Context!["allowedTargetFrameworks"]);
        Assert.Equal("net7.0", finding.Context["targetFrameworks"]);
        Assert.Equal("net7.0", finding.Context["disallowedTargetFrameworks"]);
    }

    [Fact]
    public void Evaluate_MultiTargetWithOneDisallowed_ReturnsOneDeterministicFindingPerProject()
    {
        var engine = new PolicyEngine(
            [new TargetFrameworkPolicyRule(["net10.0", "net8.0"])]);

        var result = engine.Evaluate(CreateReport(
            CreateProject(
                "src/Multi/Multi.csproj",
                ["net9.0", "net8.0", "NET9.0"])));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("net10.0,net8.0", finding.Context!["allowedTargetFrameworks"]);
        Assert.Equal("net8.0,net9.0", finding.Context["targetFrameworks"]);
        Assert.Equal("net9.0", finding.Context["disallowedTargetFrameworks"]);
    }

    [Fact]
    public void Evaluate_MissingTargetFramework_DoesNotInventViolation()
    {
        var engine = new PolicyEngine(
            [new TargetFrameworkPolicyRule(["net10.0"])]);

        var result = engine.Evaluate(CreateReport(
            CreateProject("src/Unknown/Unknown.csproj", [])));

        Assert.True(result.IsCompliant);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Evaluate_OrdersProjectFindingsByRepositoryRelativePath()
    {
        var engine = new PolicyEngine(
            [new TargetFrameworkPolicyRule(["net10.0"])]);

        var result = engine.Evaluate(CreateReport(
            CreateProject("src/Zeta/Zeta.csproj", ["net8.0"]),
            CreateProject("src/Alpha/Alpha.csproj", ["net7.0"])));

        Assert.Equal(
            new[]
            {
                "src/Alpha/Alpha.csproj",
                "src/Zeta/Zeta.csproj"
            },
            result.Findings
                .Select(static finding => finding.Scope.ProjectPath)
                .ToArray());
    }

    [Fact]
    public void Evaluate_ConfiguredWarningSeverity_IsPreserved()
    {
        var engine = new PolicyEngine(
            [new TargetFrameworkPolicyRule(["net10.0"], PolicySeverity.Warning)]);

        var result = engine.Evaluate(CreateReport(
            CreateProject("src/App/App.csproj", ["net8.0"])));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(PolicySeverity.Warning, finding.Severity);
        Assert.True(result.HasWarnings);
        Assert.False(result.HasErrors);
    }

    private static InspectionReport CreateReport(params ProjectInspection[] projects) =>
        InspectionReport.Create(
            new RepositoryMetadata("sample", null, null, null, false),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            projects,
            []);

    private static ProjectInspection CreateProject(
        string path,
        IReadOnlyList<string> targetFrameworks) =>
        new(
            path,
            Path.GetFileNameWithoutExtension(path),
            "10.0.400",
            [new ProjectSdkMetadata("Microsoft.NET.Sdk", null)],
            targetFrameworks,
            "Library",
            false,
            true,
            [],
            new ProjectClassification("library", "high", ["output-type:Library"]),
            [],
            []);
}
