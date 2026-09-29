using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Core.Policies;

public sealed class PolicyEngine
{
    private readonly IReadOnlyList<IPolicyRule> _rules;

    public PolicyEngine(IEnumerable<IPolicyRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var materializedRules = rules.ToArray();

        foreach (var rule in materializedRules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            if (string.IsNullOrWhiteSpace(rule.Code))
            {
                throw new ArgumentException(
                    "Policy rule codes cannot be null, empty, or whitespace.",
                    nameof(rules));
            }
        }

        var duplicateCode = materializedRules
            .GroupBy(static rule => rule.Code, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;

        if (duplicateCode is not null)
        {
            throw new ArgumentException(
                $"Policy rule code '{duplicateCode}' is registered more than once.",
                nameof(rules));
        }

        _rules = materializedRules;
    }

    public PolicyEvaluationResult Evaluate(InspectionReport report)
    {
        var context = PolicyEvaluationContext.FromInspection(report);
        var findings = new List<PolicyFinding>();

        foreach (var rule in _rules)
        {
            var ruleFindings = rule.Evaluate(context)
                ?? throw new InvalidOperationException(
                    $"Policy rule '{rule.Code}' returned a null findings collection.");

            foreach (var ruleFinding in ruleFindings)
            {
                ArgumentNullException.ThrowIfNull(ruleFinding);

                findings.Add(
                    new PolicyFinding(
                        rule.Code,
                        ruleFinding.Severity,
                        ruleFinding.Message,
                        ruleFinding.Scope,
                        ruleFinding.Context));
            }
        }

        return new PolicyEvaluationResult(findings.ToArray());
    }
}
