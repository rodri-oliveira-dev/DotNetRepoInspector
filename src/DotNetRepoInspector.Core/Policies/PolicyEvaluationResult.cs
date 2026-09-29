namespace DotNetRepoInspector.Core.Policies;

public sealed record PolicyEvaluationResult(IReadOnlyList<PolicyFinding> Findings)
{
    public bool IsCompliant => Findings.Count == 0;

    public bool HasWarnings =>
        Findings.Any(static finding =>
            string.Equals(finding.Severity, PolicySeverity.Warning, StringComparison.Ordinal));

    public bool HasErrors =>
        Findings.Any(static finding =>
            string.Equals(finding.Severity, PolicySeverity.Error, StringComparison.Ordinal));
}
