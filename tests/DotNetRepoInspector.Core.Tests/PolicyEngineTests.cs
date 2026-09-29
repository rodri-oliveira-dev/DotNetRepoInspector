using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Core.Policies;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class PolicyEngineTests
{
    [Fact]
    public void Evaluate_WhenRuleReturnsNoFindings_IsCompliant()
    {
        var rule = new StubPolicyRule(
            "DRP0001",
            context =>
            {
                Assert.Single(context.Projects);
                Assert.Equal("src/Sample/Sample.csproj", context.Projects[0].Path);
                Assert.Equal(["net10.0"], context.Projects[0].TargetFrameworks);

                return [];
            });
        var engine = new PolicyEngine([rule]);

        var result = engine.Evaluate(CreateReport());

        Assert.True(result.IsCompliant);
        Assert.False(result.HasWarnings);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Evaluate_AggregatesWarningAndErrorAsStructuredFindings()
    {
        var warningRule = new StubPolicyRule(
            "DRP1001",
            _ =>
            [
                new PolicyRuleFinding(
                    PolicySeverity.Warning,
                    "Repository policy produced a warning.",
                    PolicyFindingScope.Repository,
                    new Dictionary<string, string>
                    {
                        ["expected"] = "configured"
                    })
            ]);
        var errorRule = new StubPolicyRule(
            "DRP1002",
            context =>
            [
                new PolicyRuleFinding(
                    PolicySeverity.Error,
                    "Project policy produced an error.",
                    PolicyFindingScope.Project(context.Projects[0].Path),
                    new Dictionary<string, string>
                    {
                        ["targetFramework"] = context.Projects[0].TargetFrameworks[0]
                    })
            ]);
        var engine = new PolicyEngine([warningRule, errorRule]);

        var result = engine.Evaluate(CreateReport());

        Assert.False(result.IsCompliant);
        Assert.True(result.HasWarnings);
        Assert.True(result.HasErrors);

        Assert.Collection(
            result.Findings,
            warning =>
            {
                Assert.Equal("DRP1001", warning.RuleCode);
                Assert.Equal(PolicySeverity.Warning, warning.Severity);
                Assert.Equal("repository", warning.Scope.Kind);
                Assert.Null(warning.Scope.ProjectPath);
                Assert.Equal("configured", warning.Context!["expected"]);
            },
            error =>
            {
                Assert.Equal("DRP1002", error.RuleCode);
                Assert.Equal(PolicySeverity.Error, error.Severity);
                Assert.Equal("project", error.Scope.Kind);
                Assert.Equal("src/Sample/Sample.csproj", error.Scope.ProjectPath);
                Assert.Equal("net10.0", error.Context!["targetFramework"]);
            });
    }

    [Fact]
    public void Evaluate_PreservesRegistrationAndFindingOrderAcrossMultipleRules()
    {
        var firstRule = new StubPolicyRule(
            "DRP2001",
            _ =>
            [
                new PolicyRuleFinding(
                    PolicySeverity.Warning,
                    "first",
                    PolicyFindingScope.Repository),
                new PolicyRuleFinding(
                    PolicySeverity.Warning,
                    "second",
                    PolicyFindingScope.Repository)
            ]);
        var secondRule = new StubPolicyRule(
            "DRP2002",
            _ =>
            [
                new PolicyRuleFinding(
                    PolicySeverity.Error,
                    "third",
                    PolicyFindingScope.Repository)
            ]);
        var engine = new PolicyEngine([firstRule, secondRule]);

        var result = engine.Evaluate(CreateReport());

        Assert.Equal(
            ["DRP2001", "DRP2001", "DRP2002"],
            result.Findings.Select(static finding => finding.RuleCode));
        Assert.Equal(
            ["first", "second", "third"],
            result.Findings.Select(static finding => finding.Message));
    }

    [Fact]
    public void Constructor_RejectsDuplicateRuleCodes()
    {
        var firstRule = new StubPolicyRule("DRP3001", _ => []);
        var secondRule = new StubPolicyRule("DRP3001", _ => []);

        var exception = Assert.Throws<ArgumentException>(
            () => new PolicyEngine([firstRule, secondRule]));

        Assert.Contains("DRP3001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyRuleFinding_RejectsUnsupportedSeverity()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PolicyRuleFinding(
                "critical",
                "Unsupported severity.",
                PolicyFindingScope.Repository));

        Assert.Equal("severity", exception.ParamName);
    }

    private static InspectionReport CreateReport() =>
        InspectionReport.Create(
            new RepositoryMetadata(
                "sample",
                "0123456789abcdef0123456789abcdef01234567",
                "main",
                null,
                false),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [
                new ProjectInspection(
                    "src/Sample/Sample.csproj",
                    "Sample",
                    "10.0.400",
                    [new ProjectSdkMetadata("Microsoft.NET.Sdk", null)],
                    ["net10.0"],
                    "Library",
                    false,
                    true,
                    [],
                    new ProjectClassification(
                        "library",
                        "high",
                        ["output-type:Library"]),
                    [],
                    [])
            ],
            []);

    private sealed class StubPolicyRule(
        string code,
        Func<PolicyEvaluationContext, IReadOnlyList<PolicyRuleFinding>> evaluate)
        : IPolicyRule
    {
        public string Code { get; } = code;

        public IReadOnlyList<PolicyRuleFinding> Evaluate(PolicyEvaluationContext context) =>
            evaluate(context);
    }
}
