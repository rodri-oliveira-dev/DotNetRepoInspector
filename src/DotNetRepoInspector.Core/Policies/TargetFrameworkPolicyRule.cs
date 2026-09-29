namespace DotNetRepoInspector.Core.Policies;

public sealed class TargetFrameworkPolicyRule : IPolicyRule
{
    public const string RuleCode = "DRP0001";
    public const string DefaultSeverity = PolicySeverity.Error;

    private readonly HashSet<string> _allowedTargetFrameworks;

    public TargetFrameworkPolicyRule(
        IEnumerable<string> allowedTargetFrameworks,
        string severity = DefaultSeverity)
    {
        ArgumentNullException.ThrowIfNull(allowedTargetFrameworks);

        var normalizedAllowedTargetFrameworks = allowedTargetFrameworks
            .Select(static value => value?.Trim().ToLowerInvariant())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        if (normalizedAllowedTargetFrameworks.Length == 0)
        {
            throw new ArgumentException(
                "At least one allowed target framework is required.",
                nameof(allowedTargetFrameworks));
        }

        if (!PolicySeverity.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "Policy severity must be warning or error.");
        }

        AllowedTargetFrameworks = normalizedAllowedTargetFrameworks;
        Severity = severity;
        _allowedTargetFrameworks = normalizedAllowedTargetFrameworks.ToHashSet(StringComparer.Ordinal);
    }

    public string Code => RuleCode;

    public IReadOnlyList<string> AllowedTargetFrameworks { get; }

    public string Severity { get; }

    public IReadOnlyList<PolicyRuleFinding> Evaluate(PolicyEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var findings = new List<PolicyRuleFinding>();

        foreach (var project in context.Projects.OrderBy(
                     static project => project.Path,
                     StringComparer.Ordinal))
        {
            var targetFrameworks = project.TargetFrameworks
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();

            if (targetFrameworks.Length == 0)
            {
                continue;
            }

            var disallowedTargetFrameworks = targetFrameworks
                .Where(targetFramework => !_allowedTargetFrameworks.Contains(targetFramework))
                .ToArray();
            if (disallowedTargetFrameworks.Length == 0)
            {
                continue;
            }

            findings.Add(
                new PolicyRuleFinding(
                    Severity,
                    "Project targets one or more frameworks that are not allowed by policy.",
                    PolicyFindingScope.Project(project.Path),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["allowedTargetFrameworks"] = string.Join(",", AllowedTargetFrameworks),
                        ["targetFrameworks"] = string.Join(",", targetFrameworks),
                        ["disallowedTargetFrameworks"] = string.Join(",", disallowedTargetFrameworks)
                    }));
        }

        return findings;
    }
}
